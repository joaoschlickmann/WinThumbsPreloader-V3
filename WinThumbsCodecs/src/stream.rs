use windows::Win32::Foundation::{S_FALSE, S_OK};
use windows::Win32::System::Com::{
    IStream, STATFLAG_NONAME, STATSTG, STREAM_SEEK_SET,
};

pub const DEFAULT_MAX_STREAM_BYTES: usize = 128 * 1024 * 1024;
pub const SMALL_MAX_STREAM_BYTES: usize = 16 * 1024 * 1024;

pub struct StreamReader<'a> {
    stream: &'a IStream,
    total_size: u64,
}

impl<'a> StreamReader<'a> {
    /// Inspects the COM stream on the current STA thread without loading it all into RAM.
    pub fn open(stream: &'a IStream) -> Result<Self, String> {
        let mut stat = STATSTG::default();
        unsafe { stream.Stat(&mut stat, STATFLAG_NONAME) }
            .map_err(|e| format!("IStream::Stat failed: {e}"))?;
        Ok(Self {
            stream,
            total_size: stat.cbSize,
        })
    }

    pub fn total_size(&self) -> u64 {
        self.total_size
    }

    /// Reads the entire stream into memory when `total_size <= max_bytes`.
    pub fn read_all_capped(&self, max_bytes: usize) -> Result<Vec<u8>, String> {
        if self.total_size > max_bytes as u64 {
            return Err(format!(
                "Stream size {} exceeds safety cap of {} bytes",
                self.total_size, max_bytes
            ));
        }
        self.read_range(0, self.total_size as usize)
    }

    /// Reads up to `max_bytes` starting from offset 0.
    pub fn read_prefix(&self, max_bytes: usize) -> Result<Vec<u8>, String> {
        let len = self.total_size.min(max_bytes as u64) as usize;
        self.read_range(0, len)
    }

    /// Seeks to `offset` and reads up to `len` bytes.
    pub fn read_range(&self, offset: u64, len: usize) -> Result<Vec<u8>, String> {
        if offset >= self.total_size || len == 0 {
            return Ok(Vec::new());
        }
        let available = (self.total_size - offset).min(len as u64) as usize;
        let seek_pos = i64::try_from(offset)
            .map_err(|_| "Stream seek offset overflows i64".to_string())?;
        unsafe { self.stream.Seek(seek_pos, STREAM_SEEK_SET, None) }
            .map_err(|e| format!("IStream::Seek failed: {e}"))?;

        let mut buf = Vec::with_capacity(available);
        let mut remaining = available;
        let mut chunk = [0u8; 65536];

        while remaining > 0 {
            let to_read = remaining.min(chunk.len()) as u32;
            let mut bytes_read: u32 = 0;
            let hr = unsafe {
                self.stream.Read(
                    chunk.as_mut_ptr().cast(),
                    to_read,
                    Some(&mut bytes_read),
                )
            };
            if hr != S_OK && hr != S_FALSE && hr.is_err() {
                return Err(format!("IStream::Read failed with HRESULT 0x{:08X}", hr.0 as u32));
            }
            if bytes_read == 0 {
                break;
            }
            let read_len = (bytes_read as usize).min(remaining);
            buf.extend_from_slice(&chunk[..read_len]);
            remaining -= read_len;
        }

        Ok(buf)
    }

    /// Streams through up to `max_scan_bytes` looking for `start_tag` followed by `end_tag`,
    /// capping the extracted slice at `max_payload_bytes` so huge `.indd` files never OOM.
    pub fn scan_for_bounded_tag(
        &self,
        max_scan_bytes: u64,
        start_tag: &[u8],
        end_tag: &[u8],
        max_payload_bytes: usize,
    ) -> Result<Option<Vec<u8>>, String> {
        if start_tag.is_empty() || end_tag.is_empty() {
            return Ok(None);
        }

        let scan_limit = self.total_size.min(max_scan_bytes);
        unsafe { self.stream.Seek(0, STREAM_SEEK_SET, None) }
            .map_err(|e| format!("IStream::Seek failed: {e}"))?;

        const CHUNK_SIZE: usize = 256 * 1024;
        let overlap = start_tag.len().max(end_tag.len()).saturating_sub(1);
        let mut window: Vec<u8> = Vec::with_capacity(CHUNK_SIZE + overlap);
        let mut scanned: u64 = 0;
        let mut capturing = false;
        let mut captured: Vec<u8> = Vec::new();
        let mut scratch = vec![0u8; CHUNK_SIZE];

        while scanned < scan_limit {
            let to_read = ((scan_limit - scanned) as usize).min(CHUNK_SIZE) as u32;
            let mut bytes_read: u32 = 0;
            let hr = unsafe {
                self.stream.Read(
                    scratch.as_mut_ptr().cast(),
                    to_read,
                    Some(&mut bytes_read),
                )
            };
            if hr != S_OK && hr != S_FALSE && hr.is_err() {
                return Err(format!("IStream::Read failed with HRESULT 0x{:08X}", hr.0 as u32));
            }
            if bytes_read == 0 {
                break;
            }

            let read_usize = bytes_read as usize;
            scanned += read_usize as u64;
            window.extend_from_slice(&scratch[..read_usize]);

            if !capturing {
                if let Some(pos) = find_subslice(&window, start_tag) {
                    capturing = true;
                    let after_start = pos + start_tag.len();
                    if let Some(end_rel) = find_subslice(&window[after_start..], end_tag) {
                        if end_rel > max_payload_bytes {
                            return Err("Tag payload exceeds maximum limit".to_string());
                        }
                        captured.extend_from_slice(&window[after_start..after_start + end_rel]);
                        return Ok(Some(captured));
                    }
                    let tail = &window[after_start..];
                    if tail.len() > max_payload_bytes {
                        return Err("Tag payload exceeds maximum limit".to_string());
                    }
                    captured.extend_from_slice(tail);
                    window.clear();
                } else if window.len() > overlap {
                    let keep_from = window.len() - overlap;
                    window.drain(..keep_from);
                }
            } else {
                let prev_len = captured.len();
                if prev_len + window.len() > max_payload_bytes + end_tag.len() {
                    return Err("Tag payload exceeds maximum limit".to_string());
                }
                captured.extend_from_slice(&window);
                window.clear();

                let check_from = prev_len.saturating_sub(overlap);
                if let Some(end_rel) = find_subslice(&captured[check_from..], end_tag) {
                    captured.truncate(check_from + end_rel);
                    if captured.len() > max_payload_bytes {
                        return Err("Tag payload exceeds maximum limit".to_string());
                    }
                    return Ok(Some(captured));
                }
            }
        }

        Ok(None)
    }
}

pub fn find_subslice(haystack: &[u8], needle: &[u8]) -> Option<usize> {
    if needle.is_empty() || needle.len() > haystack.len() {
        return None;
    }
    haystack.windows(needle.len()).position(|w| w == needle)
}

export interface LiveAnnouncerProps {
  politeMessage: string;
  assertiveMessage: string;
}

export function LiveAnnouncer({
  politeMessage,
  assertiveMessage,
}: LiveAnnouncerProps) {
  return (
    <>
      <div
        role="status"
        aria-live="polite"
        aria-atomic="true"
        className="visually-hidden"
      >
        {politeMessage}
      </div>
      <div
        role="alert"
        aria-live="assertive"
        aria-atomic="true"
        className="visually-hidden"
      >
        {assertiveMessage}
      </div>
    </>
  );
}

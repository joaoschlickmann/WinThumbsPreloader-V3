fn main() {
    let manifest_dir = std::env::var("CARGO_MANIFEST_DIR").expect("CARGO_MANIFEST_DIR");
    let def_path = std::path::Path::new(&manifest_dir).join("exports.def");
    println!("cargo:rerun-if-changed={}", def_path.display());
    println!("cargo:rustc-cdylib-link-arg=/DEF:{}", def_path.display());
}

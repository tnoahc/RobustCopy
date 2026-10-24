//! Embeds the RobustCopy icon, version information, and application manifest in the Windows executable.

fn main() {
    println!("cargo:rerun-if-changed=../src/RobustCopy/Assets/RobustCopy.ico");
    println!("cargo:rerun-if-changed=app.manifest");

    // Resources need the Windows SDK resource compiler, so they are only embedded in MSVC builds;
    // cross-checks from other platforms build without them.
    let target_os = std::env::var("CARGO_CFG_TARGET_OS").unwrap_or_default();
    let target_env = std::env::var("CARGO_CFG_TARGET_ENV").unwrap_or_default();
    if target_os != "windows" || target_env != "msvc" {
        return;
    }

    // The manifest's assemblyIdentity needs a four-part version, so it is filled in from Cargo.toml.
    let version = ["MAJOR", "MINOR", "PATCH"]
        .map(|part| std::env::var(format!("CARGO_PKG_VERSION_{part}")).expect("Cargo sets the package version"))
        .join(".");
    let manifest = std::fs::read_to_string("app.manifest")
        .expect("failed to read app.manifest")
        .replace("$(Version)", &format!("{version}.0"));

    let mut resource = winresource::WindowsResource::new();
    resource
        .set_icon("../src/RobustCopy/Assets/RobustCopy.ico")
        .set_manifest(&manifest)
        .set("ProductName", "RobustCopy")
        .set("FileDescription", "RobustCopy")
        .set("CompanyName", "Kaleb Creative Studio")
        .set("LegalCopyright", "Copyright © 2026 Kaleb Creative Studio");
    resource.compile().expect("failed to embed Windows resources");
}

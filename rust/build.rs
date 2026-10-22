//! Embeds the RobustCopy icon, version information, and application manifest in the Windows executable.

fn main() {
    println!("cargo:rerun-if-changed=../src/RobustCopy/Assets/RobustCopy.ico");
    println!("cargo:rerun-if-changed=../src/RobustCopy/app.manifest");

    // Resources need the Windows SDK resource compiler, so they are only embedded in MSVC builds;
    // cross-checks from other platforms build without them.
    let target_os = std::env::var("CARGO_CFG_TARGET_OS").unwrap_or_default();
    let target_env = std::env::var("CARGO_CFG_TARGET_ENV").unwrap_or_default();
    if target_os != "windows" || target_env != "msvc" {
        return;
    }

    let mut resource = winresource::WindowsResource::new();
    resource
        .set_icon("../src/RobustCopy/Assets/RobustCopy.ico")
        .set_manifest_file("../src/RobustCopy/app.manifest")
        .set("ProductName", "RobustCopy")
        .set("FileDescription", "RobustCopy")
        .set("CompanyName", "Kaleb Creative Studio")
        .set("LegalCopyright", "Copyright © 2026 Kaleb Creative Studio");
    resource.compile().expect("failed to embed Windows resources");
}

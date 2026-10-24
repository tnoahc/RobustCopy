<?php
// The releases the website offers. Update this file when a release is published: the version, the
// tag, the executable's size, and the SHA-256 that the release job prints (also in RobustCopy.exe.sha256).
// Download links are built from the tag.

return [
    // RobustCopy 2 (Rust), the recommended download.
    'current' => [
        'version' => '2.0.0',
        'tag' => 'v2.0.0',
        'size' => '15.0 MB',
        'sha256' => '261363B02E143E0BE1064FE996144941BEE340672ECD1BD911D8BF522BBC8B6E',
        'runtime' => 'No runtime needed',
        'summary' => 'Download the single Windows executable. No runtime, account, or internet connection is required.',
        'highlight' => 'New in 2.0: rewritten in Rust as a single 15 MB executable.',
    ],

    // RobustCopy 1 (.NET WPF), still offered alongside.
    'classic' => [
        'version' => '1.0.2',
        'tag' => 'v1.0.2',
        'size' => '62.1 MB',
        'sha256' => 'BE566C6DDCEC13D49F55AF4CF90C1270B1177435C71F2DBD897B2E164C30A713',
        'summary' => 'RobustCopy 1 is the original .NET WPF app with the same transfer workflow and safeguards, as a self-contained executable. It stays available and receives fixes as 1.x releases.',
    ],
];

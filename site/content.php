<?php
// Repeated page content, rendered by index.php. Text is escaped when it is printed.

return [
    'repository' => 'https://github.com/tnoahc/RobustCopy',
    'publisher' => 'Kaleb Creative Studio',

    'nav' => [
        ['Features', '#features'],
        ['Privacy', '#privacy'],
        ['Requirements', '#requirements'],
    ],

    'principles' => [
        ['Local by design', 'No publisher-operated cloud service'],
        ['No telemetry', 'No analytics, ads, or tracking SDKs'],
        ['Built on Robocopy', 'Uses the Windows utility already on your PC'],
        ['Safety first', 'Pre-scan and confirmation for destructive modes'],
    ],

    'features' => [
        ['Visual strategy builder', 'Choose categorized transfer options with readable descriptions and automatic conflict handling.'],
        ['Exact command preview', 'Review the command as you configure a job, including paths, file patterns, and selected switches.'],
        ['Safe pre-scan', 'Estimate files, bytes, and planned deletions with a list-only scan before the transfer begins.'],
        ['Live transfer status', 'Follow overall progress, current file, speed, time remaining, file counts, and output in one place.'],
        ['Process controls', 'Pause and resume the active Robocopy process, or stop it permanently when plans change.'],
        ['Persistent local logs', 'Keep UTF-8 transcripts on your PC for verification and troubleshooting, with no automatic upload.'],
    ],

    'steps' => [
        ['SELECT', 'Choose locations', 'Pick a source and destination from local folders, mapped drives, or accessible UNC shares.'],
        ['REVIEW', 'Set your strategy', 'Select copy behavior, filters, reliability options, and review the generated command.'],
        ['TRANSFER', 'Start with confidence', 'Run the pre-scan, confirm any destructive effects, and monitor the transfer in real time.'],
    ],

    'safeguards' => [
        'List-only pre-scan estimates destination deletions before the transfer.',
        'A mandatory confirmation explains planned deletions and source moves.',
        'Source and destination cannot be identical or contain one another.',
        'Robocopy starts directly without routing arguments through a command shell.',
    ],

    'privacy_tags' => ['NO ACCOUNTS', 'NO ADS', 'NO ANALYTICS', 'NO CLOUD UPLOAD'],

    'requirements' => [
        ['Operating system', 'Windows 10 or 11'],
        ['Architecture', 'x64'],
        ['Runtime', 'None required'],
        ['Transfer engine', 'Windows Robocopy'],
        ['Internet connection', 'Not required'],
    ],

    // The stylized app window in the hero: Robocopy switches, and whether each is selected.
    'preview_options' => [
        ['/S', 'Subdirectories', false],
        ['/E', 'Include subfolders', true],
        ['/Z', 'Restartable mode', true],
        ['/MT:8', 'Multithreaded', false],
        ['/XO', 'Skip older', false],
        ['/XN', 'Skip newer', false],
    ],

    'preview_metrics' => [
        ['SPEED', '86 MB/s'],
        ['TIME', '00:42'],
        ['FILES', '42/61'],
    ],
];

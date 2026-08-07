# RobustCopy Microsoft Store Listing

This document contains the English-language content for the RobustCopy Microsoft Store listing. Copy these values into the corresponding rows of the CSV template exported from Partner Center.

## Product name

**CSV field:** `ProductName`

RobustCopy

## Description

**CSV field:** `Description`

RobustCopy is a focused Windows desktop interface for Microsoft Robocopy. It makes reliable file and folder transfers easier to configure, preview, monitor, and control without requiring users to build complex command lines manually.

Choose source and destination folders, select categorized transfer options, and review the generated Robocopy command before starting. RobustCopy performs a list-only pre-scan to estimate planned files, bytes, and destination deletions. Destructive modes such as Mirror, Purge, and Move require explicit confirmation.

During a transfer, RobustCopy displays live progress, current-file details, transfer speed, estimated time remaining, file counts, and Robocopy output. Active jobs can be paused, resumed, or stopped.

Run transcripts are stored locally as UTF-8 logs under the Windows local application data folder. RobustCopy does not require an account and does not include telemetry or cloud synchronization.

RobustCopy is an independent interface for the Microsoft Windows Robocopy utility.

## What's new

**CSV field:** `WhatsNew`

Leave this field empty for the first Microsoft Store submission.

For a future update, use release-specific text such as:

> Version 1.0.1 introduces the new teal RobustCopy branding icon across the application and distribution materials.

## Product features

Enter each feature as a separate CSV value. Partner Center adds the bullet formatting automatically.

| CSV field | English value |
| --- | --- |
| `ProductFeatures1` | Visual configuration for commonly used Robocopy options |
| `ProductFeatures2` | List-only pre-scan before each transfer |
| `ProductFeatures3` | Command preview before starting a job |
| `ProductFeatures4` | Live progress, transfer speed, ETA, and file counts |
| `ProductFeatures5` | Pause, resume, and stop controls |
| `ProductFeatures6` | Explicit confirmation for Mirror, Purge, and Move modes |
| `ProductFeatures7` | Local UTF-8 transfer transcripts |
| `ProductFeatures8` | Support for local, mapped-drive, and UNC paths |
| `ProductFeatures9` | Self-contained Windows x64 application |
| `ProductFeatures10` through `ProductFeatures20` | Leave empty |

## Short description

**CSV field:** `ShortDescription`

A focused Windows interface for reliable Robocopy file transfers, with safe presets, command previews, live progress, and local UTF-8 logs.

## Search terms

| CSV field | English value |
| --- | --- |
| `SearchTerms1` | robocopy |
| `SearchTerms2` | file copy |
| `SearchTerms3` | file transfer |
| `SearchTerms4` | folder sync |
| `SearchTerms5` | backup |
| `SearchTerms6` | mirror |
| `SearchTerms7` | Windows utility |

## Applicable license terms

**CSV field:** `Applicable license terms`

RobustCopy is proprietary software provided by Kaleb Creative Studio. You may install and use the application for personal or organizational purposes. You may not redistribute, sublicense, sell, modify, or reverse engineer the application except where applicable law expressly permits. The software is provided "as is," without warranties of any kind to the maximum extent permitted by law. Kaleb Creative Studio is not liable for data loss or other damages resulting from use of the application. Users are responsible for reviewing transfer commands and maintaining appropriate backups.

> This is draft store-listing language and should be reviewed before final submission if formal legal advice is required.

## Copyright and trademark information

**CSV field:** `Copyright`

(c) 2026 Kaleb Creative Studio. RobustCopy is an independent interface for Microsoft Windows Robocopy. Microsoft and Windows are trademarks of Microsoft Corporation.

## Developed by

**CSV field:** `DevelopedBy`

Kaleb Creative Studio

## Minimum requirements

| CSV field | English value |
| --- | --- |
| `RequirementsMinimum1` | Windows 10 or Windows 11 |
| `RequirementsMinimum2` | 64-bit x64 processor |
| `RequirementsMinimum3` | Access to the selected source and destination folders |
| `RequirementsMinimum4` | Additional Windows privileges may be required for backup modes |
| `RequirementsMinimum5` through `RequirementsMinimum11` | Leave empty |

## Recommended requirements

Leave `RequirementsRecommended1` through `RequirementsRecommended11` empty unless testing identifies additional recommendations.

## Store assets

| CSV field | Relative path |
| --- | --- |
| `StoreLogos1` | `logo_1x1.png` |
| `StoreLogos2` | Leave empty unless 2:3 poster artwork is prepared |
| `Screenshots1` | `screenshot1.png` |
| `Screenshots2` through `Screenshots10` | Leave empty until additional screenshots are prepared |
| `HeroArts` | Leave empty |
| `Trailers` | Leave empty |

The 1:1 logo and at least one authentic screenshot of the running application are required for the first folder import.

## Import folder layout

```text
RobustCopy-Store-Listing/
|-- Store listing RobustCopy.csv
|-- logo_1x1.png
`-- screenshot1.png
```

Keep the CSV field names and the `English` language column exactly as exported by Partner Center. Save the completed CSV with UTF-8 encoding and select the entire `RobustCopy-Store-Listing` folder when using **Import listing**.

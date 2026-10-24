<?php
declare(strict_types=1);

// RobustCopy website. Release details live in site/releases.php and repeated content in
// site/content.php. Styles, script, and images are inlined so the page is a single response.

$releases = require __DIR__ . '/site/releases.php';
$content = require __DIR__ . '/site/content.php';
$current = $releases['current'];
$classic = $releases['classic'];
$repository = $content['repository'];

function e(string $text): string
{
    return htmlspecialchars($text, ENT_QUOTES | ENT_HTML5, 'UTF-8');
}

function download_url(string $repository, array $release): string
{
    return "$repository/releases/download/{$release['tag']}/RobustCopy.exe";
}

function data_uri(string $path, string $mediaType): string
{
    $bytes = file_get_contents($path);
    if ($bytes === false) {
        throw new RuntimeException("Cannot read $path");
    }

    return "data:$mediaType;base64," . base64_encode($bytes);
}

function download_icon(): string
{
    return '<svg viewBox="0 0 24 24" fill="none" aria-hidden="true">'
        . '<path d="M12 3v12m0 0 4-4m-4 4-4-4M5 20h14" stroke="currentColor" stroke-width="2" stroke-linecap="square" stroke-linejoin="miter"/>'
        . '</svg>';
}

$title = 'RobustCopy — Reliable Windows file transfers';
?>
<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <meta name="color-scheme" content="dark">
  <meta name="theme-color" content="#0a1012">
  <link rel="icon" type="image/x-icon" href="<?= data_uri(__DIR__ . '/src/RobustCopy/Assets/RobustCopy.ico', 'image/x-icon') ?>">
  <meta name="description" content="Download RobustCopy, a private Windows desktop interface for reliable Robocopy file transfers.">
  <meta property="og:title" content="<?= e($title) ?>">
  <meta property="og:description" content="A focused desktop interface for Robocopy with safe presets, live progress, and local transcripts.">
  <meta property="og:type" content="website">
  <title><?= e($title) ?></title>
  <style>
<?php readfile(__DIR__ . '/site/styles.css'); ?>
  </style>
</head>
<body>
  <a class="skip-link" href="#main">Skip to content</a>

  <header class="site-header">
    <div class="shell nav">
      <a class="brand" href="#top" aria-label="RobustCopy home">
        <img class="brand-mark" src="<?= data_uri(__DIR__ . '/site/brand-mark.png', 'image/png') ?>" alt="">
        <span>RobustCopy</span>
      </a>
      <nav class="nav-links" aria-label="Primary navigation">
<?php foreach ($content['nav'] as [$label, $href]): ?>
        <a href="<?= e($href) ?>"><?= e($label) ?></a>
<?php endforeach; ?>
        <a href="<?= e($repository) ?>" target="_blank" rel="noreferrer">Source</a>
        <a class="nav-download" href="#download">Download</a>
      </nav>
    </div>
  </header>

  <main id="main">
    <section class="shell hero" id="top">
      <div class="hero-content">
        <p class="eyebrow">Windows file transfer utility</p>
        <h1>Reliable transfers. <span>Clear controls.</span></h1>
        <p class="hero-copy">RobustCopy turns the proven Windows Robocopy engine into a focused desktop experience with safe presets, command previews, live progress, and local transcripts.</p>
        <div class="actions">
          <a class="button primary" href="<?= e(download_url($repository, $current)) ?>">
            <?= download_icon() ?>
            Download for Windows
          </a>
          <a class="button" href="#features">Explore features</a>
          <a class="button" href="<?= e($repository) ?>" target="_blank" rel="noreferrer">View on GitHub</a>
        </div>
        <div class="download-facts" aria-label="Download details">
          <span>Version <?= e($current['version']) ?></span>
          <span>Windows 10/11 x64</span>
          <span><?= e($current['size']) ?></span>
          <span><?= e($current['runtime']) ?></span>
        </div>
        <p class="preview-note"><?= e($current['highlight']) ?> <a href="#classic">RobustCopy 1</a> is still available.</p>
      </div>

      <div class="app-window" aria-label="Stylized preview of the RobustCopy application">
        <div class="window-bar">
          <span class="window-name"><span class="window-dot"></span> RobustCopy</span>
          <span class="window-controls" aria-hidden="true"><i></i><i></i><i></i></span>
        </div>
        <div class="app-body">
          <div class="app-head">
            <div class="app-title"><small>WINDOWS FILE TRANSFER UTILITY</small>ROBUSTCOPY</div>
            <div class="app-actions" aria-hidden="true">
              <span class="start">START</span><span>PAUSE</span><span>RESUME</span><span>STOP</span>
            </div>
          </div>
          <div class="app-grid">
            <div class="app-panel">
              <div class="app-panel-head">TRANSFER LOCATIONS</div>
              <div class="app-panel-body">
                <p class="field-label">SOURCE</p>
                <div class="path-field">C:\Projects\ClientAssets</div>
                <p class="field-label">DESTINATION</p>
                <div class="path-field">E:\Backups\ClientAssets</div>
                <p class="field-label">COMMAND PREVIEW</p>
                <div class="command">robocopy.exe "C:\Projects\ClientAssets" "E:\Backups\ClientAssets" *.* /E /Z /R:3</div>
              </div>
            </div>
            <div class="app-panel">
              <div class="app-panel-head">TRANSFER STRATEGY</div>
              <div class="app-panel-body option-grid">
<?php foreach ($content['preview_options'] as [$switch, $label, $selected]): ?>
                <div class="option<?= $selected ? ' selected' : '' ?>"><b><?= e($switch) ?></b><?= e($label) ?></div>
<?php endforeach; ?>
              </div>
            </div>
            <div class="app-panel">
              <div class="app-panel-head">LIVE OVERVIEW</div>
              <div class="app-panel-body">
                <div class="progress-label"><span>Overall progress</span><b>68%</b></div>
                <div class="progress-track"><span></span></div>
                <div class="metric-grid">
<?php foreach ($content['preview_metrics'] as [$label, $value]): ?>
                  <div class="metric"><?= e($label) ?><strong><?= e($value) ?></strong></div>
<?php endforeach; ?>
                </div>
                <div class="console"><span class="ok">[copy]</span> Starting transfer…<br><span class="file">annual-report.pdf</span><br><span class="ok">68.0%</span> complete<br>[info] Restartable mode enabled</div>
              </div>
            </div>
          </div>
        </div>
      </div>
    </section>

    <div class="trust-strip" aria-label="Product principles">
<?php foreach ($content['principles'] as [$heading, $text]): ?>
      <div class="trust-item"><strong><?= e($heading) ?></strong><span><?= e($text) ?></span></div>
<?php endforeach; ?>
    </div>

    <section class="shell content-section" id="features">
      <div class="section-intro">
        <div>
          <p class="section-kicker">Purpose-built workflow</p>
          <h2>Robocopy power, without the guesswork.</h2>
        </div>
        <p>Configure dependable local, mapped-drive, and network-share transfers without memorizing switches. RobustCopy exposes the options that matter and explains their impact before anything runs.</p>
      </div>
      <div class="feature-grid">
<?php foreach ($content['features'] as $index => [$heading, $text]): ?>
        <article class="feature-card"><span class="feature-number"><?= sprintf('%02d', $index + 1) ?></span><h3><?= e($heading) ?></h3><p><?= e($text) ?></p></article>
<?php endforeach; ?>
      </div>
    </section>

    <section class="workflow content-section" aria-labelledby="workflow-title">
      <div class="shell">
        <div class="section-intro">
          <div><p class="section-kicker">Three clear steps</p><h2 id="workflow-title">From folders to finished.</h2></div>
          <p>RobustCopy keeps the transfer flow visible from configuration through completion.</p>
        </div>
        <div class="steps">
<?php foreach ($content['steps'] as $index => [$stage, $heading, $text]): ?>
          <article class="step"><span class="step-index"><?= sprintf('%02d', $index + 1) ?> / <?= e($stage) ?></span><h3><?= e($heading) ?></h3><p><?= e($text) ?></p></article>
<?php endforeach; ?>
        </div>
      </div>
    </section>

    <section class="shell content-section" aria-labelledby="safety-title">
      <div class="section-intro">
        <div><p class="section-kicker">Clear about consequences</p><h2 id="safety-title">Guardrails where they matter.</h2></div>
        <p>Powerful transfer modes can delete or move data. RobustCopy puts their effects in front of you before execution.</p>
      </div>
      <div class="safety-grid">
        <article class="safety-panel">
          <h3>Safer destructive operations</h3>
          <p>Mirror, Purge, and Move modes remain available, but they never hide what they are allowed to change.</p>
          <ul class="check-list">
<?php foreach ($content['safeguards'] as $safeguard): ?>
            <li><?= e($safeguard) ?></li>
<?php endforeach; ?>
          </ul>
          <div class="warning"><strong>Always maintain an independent backup.</strong> Review the command and destination carefully before using `/MIR`, `/PURGE`, `/MOV`, or `/MOVE`.</div>
        </article>

        <div class="side-stack">
          <article class="privacy-panel" id="privacy">
            <h3>Privacy, kept local</h3>
            <p>RobustCopy does not send your files, paths, configuration, or transcripts to <?= e($content['publisher']) ?>. Logs stay on your device until you delete them.</p>
            <div class="tag-row" aria-label="Privacy highlights">
<?php foreach ($content['privacy_tags'] as $tag): ?>
              <span class="tag"><?= e($tag) ?></span>
<?php endforeach; ?>
            </div>
          </article>
          <article class="requirements-panel" id="requirements">
            <h3>System requirements</h3>
            <p>A self-contained desktop application for modern 64-bit Windows PCs.</p>
            <div class="requirements-list">
<?php foreach ($content['requirements'] as [$label, $value]): ?>
              <div class="requirement"><span><?= e($label) ?></span><strong><?= e($value) ?></strong></div>
<?php endforeach; ?>
            </div>
          </article>
        </div>
      </div>
    </section>

    <section class="download-section" id="download">
      <div class="shell">
        <div class="download-card">
          <div>
            <p class="section-kicker">Current release · <?= e($current['version']) ?></p>
            <h2>Move files with confidence.</h2>
            <p><?= e($current['summary']) ?></p>
          </div>
          <div class="download-cta">
            <a class="button primary" href="<?= e(download_url($repository, $current)) ?>">
              <?= download_icon() ?>
              Download RobustCopy
            </a>
            <div class="download-facts"><span><?= e($current['size']) ?></span><span>Windows x64</span></div>
          </div>
        </div>
        <div class="hash-box">
          <span>SHA-256</span>
          <code id="release-hash"><?= e($current['sha256']) ?></code>
          <button class="copy-button" type="button" data-copy-target="release-hash" aria-describedby="copy-status">COPY</button>
        </div>

        <div class="preview-card" id="classic">
          <div>
            <p class="section-kicker">RobustCopy 1 · <?= e($classic['version']) ?></p>
            <h3>Prefer the .NET edition?</h3>
            <p><?= e($classic['summary']) ?></p>
          </div>
          <div class="download-cta">
            <a class="button" href="<?= e(download_url($repository, $classic)) ?>">
              <?= download_icon() ?>
              Download RobustCopy 1
            </a>
            <div class="download-facts"><span><?= e($classic['size']) ?></span><span>Windows x64</span></div>
          </div>
        </div>
        <div class="hash-box">
          <span>SHA-256</span>
          <code id="classic-hash"><?= e($classic['sha256']) ?></code>
          <button class="copy-button" type="button" data-copy-target="classic-hash" aria-describedby="copy-status">COPY</button>
        </div>
        <span id="copy-status" role="status" aria-live="polite" hidden></span>
      </div>
    </section>
  </main>

  <footer class="site-footer">
    <div class="shell footer-row">
      <span>© <?= date('Y') ?> <?= e($content['publisher']) ?>. RobustCopy is an independent interface for the Microsoft Windows Robocopy utility.</span>
      <div class="footer-links">
        <a href="<?= e($repository) ?>" target="_blank" rel="noreferrer">GitHub repository</a>
        <a href="#privacy">Privacy</a>
        <a href="#requirements">Requirements</a>
        <a href="#top">Back to top ↑</a>
      </div>
    </div>
  </footer>

  <script>
<?php readfile(__DIR__ . '/site/script.js'); ?>
  </script>
</body>
</html>

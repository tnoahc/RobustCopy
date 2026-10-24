(() => {
  const status = document.querySelector("#copy-status");

  document.querySelectorAll("[data-copy-target]").forEach((button) => button.addEventListener("click", async () => {
    const hash = document.getElementById(button.dataset.copyTarget);
    try {
      await navigator.clipboard.writeText(hash.textContent.trim());
      button.textContent = "COPIED";
      status.hidden = false;
      status.textContent = "SHA-256 checksum copied to the clipboard.";
      window.setTimeout(() => {
        button.textContent = "COPY";
        status.hidden = true;
      }, 1800);
    } catch {
      const selection = window.getSelection();
      const range = document.createRange();
      range.selectNodeContents(hash);
      selection.removeAllRanges();
      selection.addRange(range);
      status.hidden = false;
      status.textContent = "Checksum selected. Press Ctrl+C to copy it.";
    }
  }));
})();

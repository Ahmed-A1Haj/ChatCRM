document.querySelectorAll("[data-password-toggle]").forEach((toggleButton) => {
  // The password input lives in the same field wrapper. Support the actual markup
  // (.auth-password) plus older (.password-field), and fall back to the nearest input.
  const field = toggleButton.closest(".auth-password, .password-field");
  const input =
    field?.querySelector("input") ||
    toggleButton.parentElement?.querySelector("input");

  if (!input) {
    return;
  }

  const label = toggleButton.querySelector(".password-toggle-text");
  // Capture the localized "Show" strings rendered by the server; read the "Hide"
  // counterparts from data-* (also localized) with English fallbacks.
  const showText = label?.textContent?.trim() || "Show";
  const hideText = toggleButton.dataset.hideText || "Hide";
  const showLabel = toggleButton.getAttribute("aria-label") || "Show password";
  const hideLabel = toggleButton.dataset.hideLabel || "Hide password";

  toggleButton.addEventListener("click", () => {
    const reveal = input.type === "password";
    input.type = reveal ? "text" : "password";
    toggleButton.setAttribute("aria-pressed", reveal ? "true" : "false");
    toggleButton.setAttribute("aria-label", reveal ? hideLabel : showLabel);
    if (label) {
      label.textContent = reveal ? hideText : showText;
    }
  });
});

document.querySelectorAll("[data-loading-form]").forEach((form) => {
  form.addEventListener("submit", () => {
    const submitButton = form.querySelector("button[type='submit'][data-loading-text]");

    if (!submitButton) {
      return;
    }

    submitButton.dataset.originalText = submitButton.textContent ?? "";
    submitButton.textContent = submitButton.dataset.loadingText ?? "Saving...";
    submitButton.disabled = true;
  });
});

document.querySelectorAll("[data-profile-image-input]").forEach((input) => {
  input.addEventListener("change", () => {
    const fileInput = input;
    const file = fileInput.files?.[0];
    const container = document.querySelector("[data-profile-preview-container]");
    const image = container?.querySelector("[data-profile-preview-image]");
    const fallback = container?.querySelector("[data-profile-preview-fallback]");

    if (!container || !image || !fallback || !file) {
      return;
    }

    const previewUrl = URL.createObjectURL(file);
    image.setAttribute("src", previewUrl);
    image.classList.remove("d-none");
    fallback.classList.add("d-none");
  });
});

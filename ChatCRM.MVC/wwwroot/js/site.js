document.querySelectorAll("[data-password-toggle]").forEach((toggleButton) => {
  toggleButton.addEventListener("click", () => {
    const field = toggleButton.closest(".password-field");
    const input = field?.querySelector("input");

    if (!input) {
      return;
    }

    const showPassword = input.type === "password";
    input.type = showPassword ? "text" : "password";
    toggleButton.setAttribute("aria-pressed", showPassword ? "true" : "false");
    toggleButton.setAttribute("aria-label", showPassword ? "Hide password" : "Show password");

    const label = toggleButton.querySelector(".password-toggle-text");
    if (label) {
      label.textContent = showPassword ? "Hide" : "Show";
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

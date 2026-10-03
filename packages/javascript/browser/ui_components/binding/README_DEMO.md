# ROC DatePicker Demo Instructions

The ROC (民國年) DatePicker demo lives in `demo_binding.html` in this folder (the `roc_cal` field, a `DatePicker` with `useROC: true`). There is no standalone demo; the page loads ES modules, so it must be served over HTTP.

## How to Run

1. Start a static server at the repository root, e.g. `python -m http.server 8124`.

2. Open `http://localhost:8124/packages/javascript/browser/ui_components/binding/demo_binding.html` in your browser.

3. Click the "ROC Date (民國年)" input field to see the **ROC Calendar** working visually.

Opening the file directly (`file://`) does not work, because browsers block module imports from local files.

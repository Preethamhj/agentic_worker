namespace Jarvis.UI.Browser;

internal static class BrowserObservationScripts
{
    internal const string InteractiveSelector = "a[href],button,input:not([type='hidden']),textarea,select,"
        + "[role='button'],[role='link'],[role='textbox'],[role='checkbox'],[role='radio'],"
        + "[role='combobox'],[role='option'],[role='menuitem'],[role='tab'],[role='switch'],"
        + "[tabindex]:not([tabindex='-1'])";

    public static readonly string Capture = """
        (() => {
            const stateKey = "__jarvisObservationState";
            let state = window[stateKey];
            if (!state) {
                state = {
                    documentToken: `${Date.now().toString(36)}-${Math.random().toString(36).slice(2)}`,
                    domVersion: 0
                };
                window[stateKey] = state;
                if (document.documentElement) {
                    new MutationObserver(() => state.domVersion++)
                        .observe(document.documentElement, {
                            attributes: true,
                            childList: true,
                            characterData: true,
                            subtree: true
                        });
                }
            }

            const normalize = value => (value || "").replace(/\s+/g, " ").trim();
            const roleFor = element => {
                const explicitRole = element.getAttribute("role");
                if (explicitRole) return explicitRole.split(/\s+/)[0];
                const tag = element.tagName.toLowerCase();
                if (tag === "a" && element.hasAttribute("href")) return "link";
                if (tag === "button") return "button";
                if (tag === "textarea") return "textbox";
                if (tag === "select") return element.multiple ? "listbox" : "combobox";
                if (tag === "input") {
                    const type = (element.getAttribute("type") || "text").toLowerCase();
                    if (["button", "submit", "reset", "image"].includes(type)) return "button";
                    if (type === "checkbox") return "checkbox";
                    if (type === "radio") return "radio";
                    return "textbox";
                }
                return "generic";
            };
            const accessibleName = element => {
                const labelledBy = element.getAttribute("aria-labelledby");
                if (labelledBy) {
                    const text = labelledBy.split(/\s+/)
                        .map(id => document.getElementById(id)?.innerText || "")
                        .map(normalize).filter(Boolean).join(" ");
                    if (text) return text;
                }
                const ariaLabel = element.getAttribute("aria-label");
                if (ariaLabel) return normalize(ariaLabel);
                if (element.labels?.length) {
                    const text = Array.from(element.labels).map(label => normalize(label.innerText))
                        .filter(Boolean).join(" ");
                    if (text) return text;
                }
                return normalize(element.getAttribute("alt")
                    || element.getAttribute("title")
                    || element.innerText
                    || element.getAttribute("placeholder")
                    || element.textContent);
            };
            const visible = element => {
                const rect = element.getBoundingClientRect();
                if (rect.width <= 0 || rect.height <= 0) return false;
                for (let current = element; current; current = current.parentElement) {
                    const style = window.getComputedStyle(current);
                    if (style.visibility === "hidden"
                        || style.display === "none"
                        || Number(style.opacity) <= 0
                        || current.getAttribute("aria-hidden") === "true"
                        || current.hasAttribute("inert")) return false;
                }
                return true;
            };
            const selector = __JARVIS_SELECTOR__;
            const found = Array.from(document.querySelectorAll(selector));
            const elements = found.slice(0, 400).map(element => {
                const attributes = {};
                for (const name of ["type", "name", "placeholder", "aria-label", "aria-expanded",
                                    "aria-checked", "role", "title", "autocomplete"]) {
                    const value = element.getAttribute(name);
                    if (value !== null) attributes[name] = value.slice(0, 300);
                }
                if (element instanceof HTMLAnchorElement && element.href) {
                    attributes.href = element.href.slice(0, 2000);
                }
                const isDisabled = ("disabled" in element && element.disabled)
                    || element.getAttribute("aria-disabled") === "true";
                return {
                    role: roleFor(element),
                    name: accessibleName(element).slice(0, 500),
                    visibleText: normalize(element.innerText || element.textContent).slice(0, 1000),
                    visible: visible(element),
                    enabled: !isDisabled,
                    attributes
                };
            });
            const fullPageText = normalize(document.body?.innerText || "");
            const pageText = fullPageText.slice(0, 10000);
            return {
                address: location.href,
                title: document.title || "",
                name: window.name || "",
                documentToken: state.documentToken,
                domVersion: state.domVersion,
                visibleText: pageText,
                elements,
                isTruncated: found.length > elements.length
                    || fullPageText.length > pageText.length
            };
        })()
        """.Replace("__JARVIS_SELECTOR__", System.Text.Json.JsonSerializer.Serialize(InteractiveSelector), StringComparison.Ordinal);
}

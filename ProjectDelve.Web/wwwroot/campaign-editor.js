import { prepareCover } from './cover-upload.js';

const node = (tag, text = "", className = "") => {
  const result = document.createElement(tag);
  result.textContent = text;
  result.className = className;
  return result;
};
const action = (label, callback, id) => {
  const result = node("button", label);
  result.type = "button";
  if (id) result.id = id;
  result.addEventListener("click", callback);
  return result;
};

async function libraryRequest(path, options) {
  const response = await fetch(`/api/library${path}`, {
    cache: "no-store", ...options,
    headers: { "Content-Type": "application/json", ...options?.headers }
  });
  const result = response.status === 204 ? null : await response.json();
  if (!response.ok) throw new Error(result?.detail ?? result?.message ?? result?.errors?.[0]?.message ?? "The library request failed. Try again.");
  return result;
}

// Edits references only. Definitions and scenario artwork belong to the shared library.
export function mountCampaignEditor(container, { onSaved = () => {}, onCancel = () => {}, request = libraryRequest, prepareImage = prepareCover } = {}) {
  let campaign = null, scenarios = [], scenarioIds = [], baseline = "", pending = false, opened = false;
  let name, description, coverArt, list, available, search, status, form, save, heading, remove, preview;

  function values() {
    return { name: name.value, description: description.value || null, coverArt: coverArt.value || null, scenarioIds: [...scenarioIds] };
  }
  function dirty() { return opened && JSON.stringify(values()) !== baseline; }
  function canLeave() {
    if (pending) { if (status) status.textContent = "Wait for the library operation to finish."; return false; }
    return !dirty() || globalThis.confirm("Leave this campaign without saving your changes?");
  }
  function busy(value) {
    pending = value;
    for (const control of container.querySelectorAll("button,input,textarea")) control.disabled = value;
    if (!value) renderLists();
  }
  function updateCover() {
    preview.replaceChildren();
    const value = coverArt.value.trim();
    if (!value || !(/^(\/[^/\\]|https:\/\/)/i.test(value)) || /[\\\u0000-\u001f]/.test(value)) {
      preview.append(node("span", "◇", "campaign-cover-symbol"), node("small", "Cover illustration"));
      return;
    }
    const image = node("img");
    image.alt = "";
    image.referrerPolicy = "no-referrer";
    image.addEventListener("error", () => {
      if (preview.children[0] === image) preview.replaceChildren(node("small", "Artwork unavailable · your campaign still works"));
    });
    image.src = value;
    preview.append(image);
  }
  function renderLists() {
    list.replaceChildren();
    if (!scenarioIds.length) list.append(node("p", "No scenarios yet. Add scenarios from the library below.", "campaign-empty"));
    scenarioIds.forEach((id, index) => {
      const scenario = scenarios.find(item => item.id === id);
      const row = node("li", "", "campaign-scenario-row");
      row.dataset.scenarioId = id;
      row.append(node("span", String(index + 1).padStart(2, "0"), "campaign-position"));
      const label = node("div", "", "campaign-scenario-label");
      label.append(node("strong", scenario?.name ?? "Unavailable scenario"));
      if (!scenario) label.append(node("small", "This scenario was removed from the library. Remove this reference or keep its place.", "campaign-missing"));
      row.append(label);
      const controls = node("div", "", "campaign-row-actions");
      const move = offset => {
        [scenarioIds[index], scenarioIds[index + offset]] = [scenarioIds[index + offset], scenarioIds[index]];
        renderLists();
        list.querySelector(`[data-scenario-id="${id}"] button`)?.focus();
      };
      const up = action("↑", () => move(-1)); up.setAttribute("aria-label", `Move ${scenario?.name ?? "unavailable scenario"} up`); up.disabled = index === 0;
      up.dataset.campaignAction = "up";
      const down = action("↓", () => move(1)); down.setAttribute("aria-label", `Move ${scenario?.name ?? "unavailable scenario"} down`); down.disabled = index === scenarioIds.length - 1;
      down.dataset.campaignAction = "down";
      const drop = action("Remove", () => { scenarioIds.splice(index, 1); renderLists(); });
      drop.setAttribute("aria-label", `Remove ${scenario?.name ?? "unavailable scenario"} from campaign`);
      drop.dataset.campaignAction = "remove";
      controls.append(up, down, drop); row.append(controls); list.append(row);
    });
    available.replaceChildren();
    const matches = scenarios.filter(item => !scenarioIds.includes(item.id) && item.name.toLocaleLowerCase().includes(search.value.toLocaleLowerCase()));
    if (!matches.length) available.append(node("p", scenarios.length ? "No matching scenarios to add." : "Save a scenario in Designer to add it here.", "campaign-empty"));
    for (const scenario of matches) {
      const row = node("li", "", "campaign-library-row");
      row.append(node("span", scenario.name));
      const add = action("Add", () => { scenarioIds.push(scenario.id); renderLists(); });
      add.dataset.addScenario = scenario.id;
      add.setAttribute("aria-label", `Add ${scenario.name} to campaign`);
      row.append(add); available.append(row);
    }
  }
  function field(label, id, tag, value, maxLength) {
    const wrapper = node("label", label), input = node(tag);
    input.id = id; input.value = value ?? ""; input.maxLength = maxLength;
    wrapper.append(input); return { wrapper, input };
  }
  function render() {
    container.replaceChildren();
    const top = node("div", "", "campaign-editor-heading");
    heading = node("h2", campaign ? "Edit campaign" : "Create a campaign");
    top.append(heading, action("Back to campaigns", onCancel, "campaign-editor-back"));
    status = node("p", "", "campaign-editor-status"); status.id = "campaign-editor-status"; status.setAttribute("role", "status");
    status.textContent = campaign?.source === "bundled" ? "Bundled campaign · saving creates a local override." : campaign?.source === "override" ? "Local override · the bundled original remains unchanged." : "Local campaign · saved on this host.";
    form = node("form", "", "campaign-editor-form");
    const details = node("section", "", "campaign-details");
    const nameField = field("Campaign name", "campaign-name", "input", campaign?.name, 120);
    name = nameField.input; name.required = true;
    const descriptionField = field("Description (optional)", "campaign-description", "textarea", campaign?.description, 2000);
    description = descriptionField.input; description.rows = 4;
    const coverField = field("Cover art URL (optional)", "campaign-cover", "input", campaign?.coverArt, 2048);
    coverArt = coverField.input; coverArt.placeholder = "/artwork/cover.webp or https://…";
    coverArt.addEventListener("input", updateCover);
    const coverFile = node("input"); coverFile.type = "file"; coverFile.accept = "image/png,image/jpeg,image/webp"; coverFile.id = "campaign-cover-file"; coverFile.hidden = true;
    const importCover = action("Import cover image…", () => coverFile.click(), "campaign-cover-import");
    coverFile.addEventListener("change", async () => {
      const file = coverFile.files?.[0]; if (!file || pending) return;
      busy(true); status.textContent = "Importing cover image…";
      try {
        const png = await prepareImage(file);
        const result = await request("/covers", { method: "POST", headers: { "Content-Type": "image/png" }, body: png });
        coverArt.value = result.coverArt; updateCover(); status.textContent = "Cover imported locally. Save campaign to apply it.";
      } catch (error) { status.textContent = `${error.message} Existing cover retained.`; }
      finally { coverFile.value = ""; busy(false); }
    });
    preview = node("div", "", "campaign-cover-preview");
    details.append(nameField.wrapper, descriptionField.wrapper, coverField.wrapper, coverFile, importCover,
      node("p", "PNG, JPEG or WebP, up to 12 MB and 4096 × 4096 pixels.", "campaign-hint"),
      node("small", "Choose an atmospheric illustration. Descriptions and cover art are visible before play; avoid maps, enemies and other hidden details."), preview);
    const contents = node("section", "", "campaign-contents");
    contents.append(node("h3", "Scenario order"), node("p", "Players can start any scenario. Each begins a fresh, independent game.", "campaign-hint"));
    list = node("ol", "", "campaign-scenario-list"); list.id = "campaign-scenarios";
    const searchField = field("Add from the scenario library", "campaign-scenario-search", "input", "", 160);
    search = searchField.input; search.type = "search"; search.placeholder = "Find a scenario…";
    search.addEventListener("input", renderLists);
    available = node("ul", "", "campaign-library-list"); available.id = "campaign-library-scenarios";
    contents.append(list, searchField.wrapper, available);
    const actions = node("div", "", "campaign-editor-actions");
    save = node("button", "Save campaign", "designer-primary"); save.id = "campaign-save"; save.type = "submit";
    actions.append(save);
    if (campaign) {
      const confirmation = node("div", "", "campaign-delete-confirm"); confirmation.hidden = true;
      const bundled = campaign.source === "bundled" || campaign.source === "override";
      confirmation.append(node("p", bundled ? "Hide this campaign locally? The bundled version can be restored from the library. Scenarios remain available." : "Delete this campaign? All saved scenarios will remain in the library."),
        action(bundled ? "Hide campaign locally" : "Delete campaign permanently", async () => {
          if (pending) return;
          busy(true); status.textContent = "Deleting campaign…";
          try {
            await request(`/campaigns/${encodeURIComponent(campaign.id)}?revision=${campaign.revision}${campaign.changeToken ? `&changeToken=${encodeURIComponent(campaign.changeToken)}` : ""}`, { method: "DELETE" });
            opened = false; status.textContent = "Campaign deleted."; busy(false); onSaved(null);
          } catch (error) { status.textContent = error.message; }
          finally { busy(false); }
        }, "campaign-delete-confirm"), action("Keep campaign", () => { confirmation.hidden = true; }));
      remove = action(bundled ? "Hide campaign…" : "Delete campaign…", () => { confirmation.hidden = false; }, "campaign-delete");
      actions.append(remove, confirmation);
      if (campaign.source === "override") {
        const restoreConfirmation = node("div", "", "campaign-delete-confirm"); restoreConfirmation.hidden = true;
        restoreConfirmation.append(node("p", "Discard this local override and unsaved campaign edits? The bundled campaign will replace them."),
          action("Restore bundled campaign", async () => {
            if (pending) return;
            busy(true); status.textContent = "Restoring bundled campaign…";
            try {
              const restored = await request(`/campaigns/${encodeURIComponent(campaign.id)}/restore`, { method: "POST", body: JSON.stringify({ revision: campaign.revision, changeToken: campaign.changeToken }) });
              opened = false; busy(false); onSaved(restored);
            } catch (error) { status.textContent = `${error.message} Local edits retained.`; }
            finally { busy(false); }
          }, "campaign-restore-confirm"), action("Keep local edits", () => { restoreConfirmation.hidden = true; }));
        actions.append(action("Restore bundled version…", () => { restoreConfirmation.hidden = false; }, "campaign-restore"), restoreConfirmation);
      }
    }
    form.append(details, contents, actions);
    form.addEventListener("submit", async event => {
      event.preventDefault();
      if (pending || !form.reportValidity()) return;
      busy(true); status.textContent = "Saving campaign…";
      try {
        const body = { ...values(), ...(campaign ? { revision: campaign.revision, changeToken: campaign.changeToken } : {}) };
        const saved = await request(campaign ? `/campaigns/${encodeURIComponent(campaign.id)}` : "/campaigns", {
          method: campaign ? "PUT" : "POST", body: JSON.stringify(body)
        });
        campaign = saved; baseline = JSON.stringify(values()); status.textContent = "Campaign saved.";
        busy(false); onSaved(saved);
      } catch (error) { status.textContent = error.message; }
      finally { busy(false); }
    });
    container.append(top, status, form);
    renderLists(); updateCover();
  }
  async function open(id = null) {
    if (pending) return false;
    container.replaceChildren(node("p", "Opening campaign editor…"));
    pending = true;
    try {
      const library = await request("");
      campaign = id ? library.campaigns.find(item => item.id === id) : null;
      if (id && !campaign) throw new Error("This campaign is no longer in the library.");
      scenarios = library.scenarios;
      scenarioIds = [...(campaign?.scenarioIds ?? [])];
      render(); opened = true; baseline = JSON.stringify(values()); name.focus();
      return true;
    } catch (error) {
      opened = false;
      container.replaceChildren(node("p", error.message, "campaign-editor-status"),
        action("Retry", () => open(id), "campaign-editor-retry"), action("Back to campaigns", onCancel, "campaign-editor-back"));
      return false;
    } finally { pending = false; }
  }
  globalThis.addEventListener?.("beforeunload", event => {
    if (!container.hidden && dirty()) { event.preventDefault(); event.returnValue = ""; }
  });
  return { open, canLeave };
}

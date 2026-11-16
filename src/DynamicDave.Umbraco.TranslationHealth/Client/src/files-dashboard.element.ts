import { css, html, customElement, state, repeat, nothing } from '@umbraco-cms/backoffice/external/lit';
import { UmbLitElement } from '@umbraco-cms/backoffice/lit-element';
import { umbConfirmModal } from '@umbraco-cms/backoffice/modal';
import type { UUIBooleanInputEvent, UUIPaginationEvent, UUISelectEvent } from '@umbraco-cms/backoffice/external/uui';
import { TranslationDictionaryService, TranslationHealthService } from './api/index.js';
import type {
  ApplyOutcome,
  ApplyResponse,
  ImportDetailResponse,
  ImportFileFormat,
  ImportModel,
  ImportOutcome,
  ImportPlanResponse,
  LanguageModel,
  PlanItemModel,
  UndoResponse,
} from './api/index.js';
import {
  PAGE_SIZE,
  errorCode,
  errorTerm,
  fileNameFrom,
  notify,
  outcomeColor,
  outcomeTerm,
  reasonTerm,
  saveBlob,
  sharedStyles,
} from './shared.js';

/** The server's upload limit (DictionaryTranslationsController.MaxUploadBytes): a larger file is refused before it is sent. */
const MAX_UPLOAD_BYTES = 10_000_000;

/** The server's page limit for plan items (Paging.MaxPlanItemsTake): a large test run loads in few requests. */
const ITEMS_TAKE = 1000;

/** Outcomes that can be written; the others (unchanged, skipped, error) have no checkbox. */
const SELECTABLE: ImportOutcome[] = ['NewValue', 'Overwrite', 'NewKey'];
const isSelectable = (item: PlanItemModel) => SELECTABLE.includes(item.outcome);

const OUTCOME_ORDER: ImportOutcome[] = ['NewValue', 'Overwrite', 'NewKey', 'Unchanged', 'SkippedHasValue', 'SkippedUnknownKey', 'Error'];
const APPLY_OUTCOMES: ApplyOutcome[] = ['Applied', 'Changed', 'Skipped'];

/** The localized message of a failed request: by error code, else by HTTP status. */
function failureTerm(status: number | undefined, code: string | undefined, fallbacks: Record<number, string> = {}) {
  if (code) {
    const term = errorTerm(code);
    if (term !== 'ddTranslationHealth_loadFailed') return term;
  }
  if (status === 403) return 'ddTranslationHealth_forbidden';
  return (status && fallbacks[status]) || 'ddTranslationHealth_loadFailed';
}

/** Export of dictionary items as CSV or XLIFF (spec 5.2). */
@customElement('dd-translation-health-export')
export class DdTranslationHealthExportElement extends UmbLitElement {
  @state() private _languages: LanguageModel[] = [];
  @state() private _groups: string[] = [];
  @state() private _format: ImportFileFormat = 'Csv';
  @state() private _scope: 'all' | 'missing' | 'group' = 'all';
  @state() private _culture = '';
  @state() private _group = '';
  @state() private _busy = false;

  override connectedCallback() {
    super.connectedCallback();
    void this.#load();
  }

  async #load() {
    try {
      const [languages, groups] = await Promise.all([
        TranslationHealthService.getLanguages({ throwOnError: false }),
        TranslationDictionaryService.getDictionaryGroups({ throwOnError: false }),
      ]);
      this._languages = languages.data ?? [];
      this._groups = [...(groups.data ?? [])].sort((a, b) => a.localeCompare(b));
    } catch {
      // The form still works for "all items" in CSV.
    }
  }

  /** The languages an export can target: XLIFF needs a non-default language; "missing in" can use any. */
  #cultures() {
    return this._format === 'Xliff' ? this._languages.filter((l) => !l.isDefault) : this._languages;
  }

  #culture() {
    const cultures = this.#cultures();
    return cultures.some((l) => l.culture === this._culture) ? this._culture : (cultures[0]?.culture ?? '');
  }

  async #export() {
    const culture = this.#culture();
    this._busy = true;
    let blob: Blob | undefined;
    let response: Response | undefined;
    let error: unknown;
    try {
      const result = await TranslationDictionaryService.exportDictionary({
        throwOnError: false,
        parseAs: 'blob',
        query: {
          format: this._format,
          culture: this._format === 'Xliff' ? culture : undefined,
          missingIn: this._scope === 'missing' ? culture : undefined,
          group: this._scope === 'group' && this._group ? this._group : undefined,
        },
      });
      blob = result.data instanceof Blob ? result.data : undefined;
      response = result.response;
      error = result.error;
    } catch {
      blob = undefined;
    }
    this._busy = false;
    if (!blob) {
      let code = errorCode(error);
      // The error body of a blob request arrives as text: read the code from it.
      if (!code && error instanceof Blob) {
        try {
          code = errorCode(JSON.parse(await error.text()));
        } catch {
          code = undefined;
        }
      }
      void notify(this, 'danger', this.localize.term(failureTerm(response?.status, code, { 400: 'ddTranslationHealth_exportFailed' })));
      return;
    }
    saveBlob(blob, fileNameFrom(response, this._format === 'Xliff' ? 'dictionary.xlf' : 'dictionary.csv'));
  }

  override render() {
    const culture = this.#culture();
    const cultureOptions = this.#cultures().map((l) => ({ name: l.name, value: l.culture, selected: l.culture === culture }));
    const showCulture = this._format === 'Xliff' || this._scope === 'missing';
    return html`<uui-box headline=${this.localize.term('ddTranslationHealth_exportTitle')}>
      <div class="filters">
        <label>
          ${this.localize.term('ddTranslationHealth_format')}
          <uui-select
            label=${this.localize.term('ddTranslationHealth_format')}
            .options=${[
              { name: 'CSV', value: 'Csv', selected: this._format === 'Csv' },
              { name: 'XLIFF 1.2', value: 'Xliff', selected: this._format === 'Xliff' },
            ]}
            @change=${(e: UUISelectEvent) => (this._format = String(e.target.value) as ImportFileFormat)}></uui-select>
        </label>
        ${showCulture
          ? html`<label>
              ${this.localize.term(this._format === 'Xliff' ? 'ddTranslationHealth_targetLanguage' : 'ddTranslationHealth_language')}
              <uui-select
                label=${this.localize.term('ddTranslationHealth_language')}
                .options=${cultureOptions}
                @change=${(e: UUISelectEvent) => (this._culture = String(e.target.value))}></uui-select>
            </label>`
          : nothing}
        <label>
          ${this.localize.term('ddTranslationHealth_items')}
          <uui-select
            label=${this.localize.term('ddTranslationHealth_items')}
            .options=${[
              { name: this.localize.term('ddTranslationHealth_exportAll'), value: 'all', selected: this._scope === 'all' },
              { name: this.localize.term('ddTranslationHealth_exportMissingIn') + ' …', value: 'missing', selected: this._scope === 'missing' },
              { name: this.localize.term('ddTranslationHealth_exportGroup'), value: 'group', selected: this._scope === 'group' },
            ]}
            @change=${(e: UUISelectEvent) => (this._scope = String(e.target.value) as 'all' | 'missing' | 'group')}></uui-select>
        </label>
        ${this._scope === 'group'
          ? html`<label>
              ${this.localize.term('ddTranslationHealth_group')}
              <uui-select
                label=${this.localize.term('ddTranslationHealth_group')}
                .options=${[
                  { name: this.localize.term('ddTranslationHealth_allGroups'), value: '', selected: this._group === '' },
                  ...this._groups.map((g) => ({ name: g, value: g, selected: g === this._group })),
                ]}
                @change=${(e: UUISelectEvent) => (this._group = String(e.target.value))}></uui-select>
            </label>`
          : nothing}
        <uui-button
          look="primary"
          label=${this.localize.term('ddTranslationHealth_export')}
          ?disabled=${this._busy || (this._format === 'Xliff' && !culture)}
          .state=${this._busy ? 'waiting' : undefined}
          @click=${this.#export}></uui-button>
      </div>
    </uui-box>`;
  }

  static override styles = [sharedStyles, css`:host { padding: 0; }`];
}

/** Upload, test run with a selection per item, and apply (spec 5.3). */
@customElement('dd-translation-health-import')
export class DdTranslationHealthImportElement extends UmbLitElement {
  @state() private _plan?: ImportPlanResponse;
  /** All items of the plan, loaded in pages of 100 after the upload. */
  @state() private _items: PlanItemModel[] = [];
  @state() private _loadingItems = false;
  @state() private _selected = new Set<number>();
  @state() private _filter?: ImportOutcome;
  @state() private _page = 1;
  @state() private _overwrite = false;
  @state() private _createKeys = false;
  @state() private _uploading = false;
  @state() private _applying = false;
  @state() private _message?: string;
  @state() private _result?: { response: ApplyResponse; items: Map<number, PlanItemModel>; sent: Set<number> };

  #file?: File;
  #request = 0;

  override disconnectedCallback() {
    super.disconnectedCallback();
    this.#request++;
  }

  #chooseFile() {
    this.shadowRoot?.querySelector<HTMLInputElement>('#file')?.click();
  }

  #fileChosen(e: Event) {
    const input = e.target as HTMLInputElement;
    const file = input.files?.[0];
    input.value = ''; // the same file can be chosen again
    if (!file) return;
    if (file.size > MAX_UPLOAD_BYTES) {
      // The server would refuse the request before it can answer with a reason.
      this.#clear();
      this.#file = undefined;
      this._result = undefined;
      this._message = this.localize.term('ddTranslationHealth_fileErrorTooLarge');
      return;
    }
    this.#file = file;
    void this.#upload();
  }

  #optionChanged(apply: () => void) {
    apply();
    // The test run depends on the options: run it again with the same file.
    if (this.#file) void this.#upload();
  }

  #clear() {
    this.#request++;
    this._plan = undefined;
    this._items = [];
    this._selected = new Set();
    this._filter = undefined;
    this._page = 1;
    this._loadingItems = false;
  }

  async #upload() {
    const file = this.#file;
    if (!file) return;
    this.#clear();
    this._result = undefined;
    this._message = undefined;
    this._uploading = true;
    const requestId = this.#request;
    try {
      const { data, error, response } = await TranslationDictionaryService.createImportPlan({
        throwOnError: false,
        body: { file },
        query: { overwrite: this._overwrite, createKeys: this._createKeys },
      });
      if (requestId !== this.#request) return;
      if (data && 'planId' in data) {
        this._plan = data;
        this._items = data.items;
        this._selected = new Set(data.items.filter((i) => i.selectedByDefault).map((i) => i.id));
        void this.#loadRemainingItems(data, requestId);
      } else {
        this._message = this.localize.term(failureTerm(response?.status, errorCode(error)));
      }
    } catch {
      if (requestId === this.#request) this._message = this.localize.term('ddTranslationHealth_loadFailed');
    } finally {
      if (requestId === this.#request) this._uploading = false;
    }
  }

  /** The plan response carries the first page; the selection needs every item, so the rest is fetched in pages. */
  async #loadRemainingItems(plan: ImportPlanResponse, requestId: number) {
    this._loadingItems = plan.total > plan.items.length;
    try {
      while (this._items.length < plan.total) {
        const { data, response } = await TranslationDictionaryService.getImportPlanItems({
          throwOnError: false,
          path: { planId: plan.planId },
          query: { skip: this._items.length, take: ITEMS_TAKE },
        });
        if (requestId !== this.#request) return;
        if (!data || data.items.length === 0) {
          if (response?.status === 404) this.#expired();
          else this._message = this.localize.term('ddTranslationHealth_loadFailed');
          break;
        }
        this._items = [...this._items, ...data.items];
        const selected = new Set(this._selected);
        for (const item of data.items) if (item.selectedByDefault) selected.add(item.id);
        this._selected = selected;
      }
    } catch {
      if (requestId === this.#request) this._message = this.localize.term('ddTranslationHealth_loadFailed');
    }
    if (requestId === this.#request) this._loadingItems = false;
  }

  #expired() {
    this.#clear();
    this.#file = undefined;
    this._message = this.localize.term('ddTranslationHealth_planExpired');
  }

  #visible() {
    return this._filter ? this._items.filter((i) => i.outcome === this._filter) : this._items;
  }

  #toggle(id: number, checked: boolean) {
    const selected = new Set(this._selected);
    if (checked) selected.add(id);
    else selected.delete(id);
    this._selected = selected;
  }

  #toggleAll(checked: boolean) {
    const selected = new Set(this._selected);
    for (const item of this.#visible()) {
      if (!isSelectable(item)) continue;
      if (checked) selected.add(item.id);
      else selected.delete(item.id);
    }
    this._selected = selected;
  }

  async #apply() {
    const plan = this._plan;
    if (!plan || this._selected.size === 0) return;
    const ids = this._items.filter((i) => this._selected.has(i.id) && isSelectable(i)).map((i) => i.id);
    if (ids.length === 0) return;
    try {
      await umbConfirmModal(this, {
        headline: this.localize.term('ddTranslationHealth_apply'),
        content: this.localize.term('ddTranslationHealth_applyConfirm', ids.length),
        confirmLabel: this.localize.term('ddTranslationHealth_apply'),
        color: 'positive',
      });
    } catch {
      return; // cancelled
    }
    this._applying = true;
    this._message = undefined;
    const itemsById = new Map(this._items.map((i) => [i.id, i]));
    try {
      const { data, error, response } = await TranslationDictionaryService.applyImportPlan({
        throwOnError: false,
        path: { planId: plan.planId },
        body: { selectedItemIds: ids },
      });
      if (data && 'applied' in data) {
        // The plan is consumed by an apply: it cannot be applied again.
        this.#clear();
        this.#file = undefined;
        this._result = { response: data, items: itemsById, sent: new Set(ids) };
        this.dispatchEvent(new CustomEvent('dd-translation-health-imports-changed', { bubbles: true, composed: true }));
      } else if (response?.status === 404) {
        this.#expired();
      } else {
        this._message = this.localize.term(failureTerm(response?.status, errorCode(error), { 500: 'ddTranslationHealth_applyFailed' }));
      }
    } catch {
      this._message = this.localize.term('ddTranslationHealth_loadFailed');
    }
    this._applying = false;
  }

  #count(outcome: ImportOutcome) {
    return this._plan?.counts.find((c) => c.outcome === outcome)?.count ?? 0;
  }

  override render() {
    return html`<uui-box headline=${this.localize.term('ddTranslationHealth_importTitle')}>
      <p class="hint">${this.localize.term('ddTranslationHealth_importHint')}</p>
      <div class="actions">
        <input id="file" type="file" accept=".csv,.xlf,.xliff,text/csv" hidden @change=${this.#fileChosen} />
        <uui-button
          look="primary"
          label=${this.localize.term('ddTranslationHealth_chooseFile')}
          ?disabled=${this._uploading}
          .state=${this._uploading ? 'waiting' : undefined}
          @click=${this.#chooseFile}></uui-button>
        ${this._plan ? html`<span class="muted">${this._plan.fileName}</span>` : nothing}
        <uui-checkbox
          label=${this.localize.term('ddTranslationHealth_overwrite')}
          ?checked=${this._overwrite}
          @change=${(e: UUIBooleanInputEvent) => this.#optionChanged(() => (this._overwrite = e.target.checked))}
          >${this.localize.term('ddTranslationHealth_overwrite')}</uui-checkbox
        >
        <uui-checkbox
          label=${this.localize.term('ddTranslationHealth_createKeys')}
          ?checked=${this._createKeys}
          @change=${(e: UUIBooleanInputEvent) => this.#optionChanged(() => (this._createKeys = e.target.checked))}
          >${this.localize.term('ddTranslationHealth_createKeys')}</uui-checkbox
        >
      </div>
      ${this._message ? html`<p class="error">${this._message}</p>` : nothing} ${this._result ? this.#renderResult(this._result) : nothing}
      ${this._plan ? this.#renderPlan(this._plan) : nothing}
    </uui-box>`;
  }

  #renderResult(result: { response: ApplyResponse; items: Map<number, PlanItemModel>; sent: Set<number> }) {
    const { response, items, sent } = result;
    // The response lists every item of the plan; the ones that were not selected are not part of this result.
    const outcomes = response.items.filter((i) => sent.has(i.itemId));
    const attention = outcomes.filter((i) => i.outcome !== 'Applied');
    return html`<div class="result">
      <h4>${this.localize.term('ddTranslationHealth_results')}</h4>
      <div class="tags">
        ${APPLY_OUTCOMES.map((o) => ({ o, n: outcomes.filter((i) => i.outcome === o).length }))
          .filter(({ n }) => n > 0)
          .map(({ o, n }) => html`<uui-tag color=${o === 'Applied' ? 'positive' : 'warning'}>${this.localize.term(`ddTranslationHealth_applyOutcome${o}`)}: ${n}</uui-tag>`)}
      </div>
      ${attention.length > 0
        ? html`<ul>
            ${attention.map((a) => {
              const item = items.get(a.itemId);
              return html`<li>
                <code>${item?.key ?? a.itemId}</code> ${item?.culture ?? ''} —
                ${this.localize.term(`ddTranslationHealth_applyOutcome${a.outcome}`)}
              </li>`;
            })}
          </ul>`
        : nothing}
    </div>`;
  }

  #renderPlan(plan: ImportPlanResponse) {
    const visible = this.#visible();
    const pages = Math.max(1, Math.ceil(visible.length / PAGE_SIZE));
    const rows = visible.slice((this._page - 1) * PAGE_SIZE, this._page * PAGE_SIZE);
    const selectable = visible.filter(isSelectable);
    const allSelected = selectable.length > 0 && selectable.every((i) => this._selected.has(i.id));
    const counts = OUTCOME_ORDER.map((o) => ({ outcome: o, count: this.#count(o) })).filter((c) => c.count > 0);
    return html`
      <h4>${this.localize.term('ddTranslationHealth_testRun')}</h4>
      ${plan.unknownColumns.length > 0
        ? html`<p class="hint">${this.localize.term('ddTranslationHealth_unknownColumns', plan.unknownColumns.join(', '))}</p>`
        : nothing}
      <div class="tags">
        <button type="button" class="filter ${this._filter === undefined ? 'active' : ''}" @click=${() => this.#setFilter(undefined)}>
          <uui-tag look="secondary">${this.localize.term('ddTranslationHealth_all')}: ${plan.total}</uui-tag>
        </button>
        ${counts.map(
          (c) => html`<button
            type="button"
            class="filter ${this._filter === c.outcome ? 'active' : ''}"
            @click=${() => this.#setFilter(this._filter === c.outcome ? undefined : c.outcome)}>
            <uui-tag color=${outcomeColor(c.outcome)} look="secondary">${this.localize.term(outcomeTerm(c.outcome))}: ${c.count}</uui-tag>
          </button>`,
        )}
      </div>
      ${this._loadingItems ? html`<uui-loader-bar></uui-loader-bar>` : nothing}
      ${rows.length === 0
        ? html`<p class="muted">${this.localize.term('ddTranslationHealth_noRows')}</p>`
        : html`<div class="table-wrap">
              <uui-table>
                <uui-table-head>
                  <uui-table-head-cell>
                    <uui-checkbox
                      label=${this.localize.term('ddTranslationHealth_selectAll')}
                      title=${this.localize.term('ddTranslationHealth_selectAll')}
                      ?checked=${allSelected}
                      ?disabled=${selectable.length === 0}
                      @change=${(e: UUIBooleanInputEvent) => this.#toggleAll(e.target.checked)}
                      ><span hidden></span></uui-checkbox>
                  </uui-table-head-cell>
                  <uui-table-head-cell>${this.localize.term('ddTranslationHealth_record')}</uui-table-head-cell>
                  <uui-table-head-cell>${this.localize.term('ddTranslationHealth_key')}</uui-table-head-cell>
                  <uui-table-head-cell>${this.localize.term('ddTranslationHealth_language')}</uui-table-head-cell>
                  <uui-table-head-cell>${this.localize.term('ddTranslationHealth_currentValue')}</uui-table-head-cell>
                  <uui-table-head-cell>${this.localize.term('ddTranslationHealth_newValue')}</uui-table-head-cell>
                  <uui-table-head-cell>${this.localize.term('ddTranslationHealth_outcome')}</uui-table-head-cell>
                </uui-table-head>
                ${repeat(rows, (i) => i.id, (i) => this.#renderItem(i))}
              </uui-table>
            </div>
            ${pages > 1
              ? html`<uui-pagination
                  .total=${pages}
                  .current=${this._page}
                  @change=${(e: UUIPaginationEvent) => (this._page = e.target.current)}></uui-pagination>`
              : nothing}`}
      <div class="actions">
        <uui-button
          look="primary"
          color="positive"
          label=${this.localize.term('ddTranslationHealth_apply')}
          ?disabled=${this._applying || this._loadingItems || this._selected.size === 0}
          .state=${this._applying ? 'waiting' : undefined}
          @click=${this.#apply}></uui-button>
        <span class="muted">${this.localize.term('ddTranslationHealth_selectedCount', this._selected.size)}</span>
      </div>
    `;
  }

  #setFilter(outcome: ImportOutcome | undefined) {
    this._filter = outcome;
    this._page = 1;
  }

  #renderItem(item: PlanItemModel) {
    const selectable = isSelectable(item);
    return html`<uui-table-row>
      <uui-table-cell>
        ${selectable
          ? html`<uui-checkbox
              label=${this.localize.term('ddTranslationHealth_select')}
              ?checked=${this._selected.has(item.id)}
              @change=${(e: UUIBooleanInputEvent) => this.#toggle(item.id, e.target.checked)}
              ><span hidden></span></uui-checkbox>`
          : nothing}
      </uui-table-cell>
      <uui-table-cell>${item.rowNumber}</uui-table-cell>
      <uui-table-cell class="break"><code>${item.key}</code></uui-table-cell>
      <uui-table-cell>${item.culture || '—'}</uui-table-cell>
      <uui-table-cell class="break">${item.currentValue || html`<span class="muted">—</span>`}</uui-table-cell>
      <uui-table-cell class="break">${item.newValue}</uui-table-cell>
      <uui-table-cell>
        <uui-tag color=${outcomeColor(item.outcome)}>${this.localize.term(outcomeTerm(item.outcome))}</uui-tag>
        ${item.outcome === 'Error' && item.reason !== 'None' ? html`<div class="sub">${this.localize.term(reasonTerm(item.reason))}</div>` : nothing}
        ${selectable && item.sourceChanged
          ? html`<div class="source-changed">
              <uui-tag color="warning">${this.localize.term('ddTranslationHealth_sourceChanged')}</uui-tag>
              <div class="sub">${this.localize.term('ddTranslationHealth_sourceInFile')}: ${item.fileSource}</div>
              <div class="sub">${this.localize.term('ddTranslationHealth_sourceNow')}: ${item.currentSource}</div>
            </div>`
          : nothing}
      </uui-table-cell>
    </uui-table-row>`;
  }

  static override styles = [
    sharedStyles,
    css`
      :host {
        padding: 0;
      }
      h4 {
        margin: var(--uui-size-space-5) 0 0;
      }
      .sub {
        font-size: var(--uui-type-small-size);
        color: var(--uui-color-text-alt);
        margin-top: var(--uui-size-space-1);
        max-width: 40ch;
        overflow-wrap: anywhere;
      }
      .source-changed {
        margin-top: var(--uui-size-space-2);
      }
      uui-table-cell {
        vertical-align: top;
      }
      uui-table-cell.break {
        max-width: 40ch;
      }
    `,
  ];
}

/** History of applied imports with a detail view and undo (spec 5.4). */
@customElement('dd-translation-health-history')
export class DdTranslationHealthHistoryElement extends UmbLitElement {
  @state() private _imports: ImportModel[] = [];
  @state() private _total = 0;
  @state() private _page = 1;
  @state() private _loading = true;
  @state() private _failed = false;
  @state() private _open?: ImportModel;
  @state() private _detail?: ImportDetailResponse;
  @state() private _detailPage = 1;
  @state() private _detailLoading = false;
  @state() private _undoing = false;
  @state() private _message?: string;
  @state() private _undone?: { key: string; result: UndoResponse };

  #listRequest = 0;
  #detailRequest = 0;
  #connected = false;

  override connectedCallback() {
    super.connectedCallback();
    this.#connected = true;
    void this.reload();
  }

  override disconnectedCallback() {
    super.disconnectedCallback();
    this.#connected = false;
    this.#listRequest++;
    this.#detailRequest++;
  }

  /** Reloads the list; called by the dashboard after an import was applied. */
  async reload() {
    const requestId = ++this.#listRequest;
    this._loading = true;
    let items: ImportModel[] = [];
    let total = 0;
    let failed = false;
    try {
      const { data } = await TranslationDictionaryService.getImports({
        throwOnError: false,
        query: { skip: (this._page - 1) * PAGE_SIZE, take: PAGE_SIZE },
      });
      failed = !data;
      items = data?.items ?? [];
      total = data?.total ?? 0;
    } catch {
      failed = true;
    }
    if (requestId !== this.#listRequest || !this.#connected) return;
    this._imports = items;
    this._total = total;
    this._failed = failed;
    this._loading = false;
    // Keep the open import's state (applied / undone) in step with the list.
    const open = this._open && items.find((i) => i.key === this._open?.key);
    if (open) this._open = open;
  }

  async #openDetail(item: ImportModel, page = 1) {
    // Never show the rows of the previously opened import under another one.
    if (this._open?.key !== item.key) this._detail = undefined;
    this._open = item;
    this._detailPage = page;
    this._message = undefined;
    if (this._undone?.key !== item.key) this._undone = undefined;
    const requestId = ++this.#detailRequest;
    this._detailLoading = true;
    let detail: ImportDetailResponse | undefined;
    try {
      const { data } = await TranslationDictionaryService.getImport({
        throwOnError: false,
        path: { key: item.key },
        query: { skip: (page - 1) * PAGE_SIZE, take: PAGE_SIZE },
      });
      detail = data && 'rows' in data ? data : undefined;
    } catch {
      detail = undefined;
    }
    if (requestId !== this.#detailRequest || !this.#connected) return;
    this._detail = detail;
    this._detailLoading = false;
    if (!detail) this._message = this.localize.term('ddTranslationHealth_loadFailed');
  }

  async #undo(item: ImportModel) {
    try {
      await umbConfirmModal(this, {
        headline: this.localize.term('ddTranslationHealth_undo'),
        content: this.localize.term('ddTranslationHealth_undoConfirm'),
        confirmLabel: this.localize.term('ddTranslationHealth_undo'),
        color: 'danger',
      });
    } catch {
      return; // cancelled
    }
    this._undoing = true;
    this._message = undefined;
    try {
      const { data, error, response } = await TranslationDictionaryService.undoImport({ throwOnError: false, path: { key: item.key } });
      if (data && 'restored' in data) {
        this._undone = { key: item.key, result: data };
        await this.reload();
        if (this.#connected) await this.#openDetail(this._imports.find((i) => i.key === item.key) ?? item, this._detailPage);
      } else {
        this._message = this.localize.term(
          failureTerm(response?.status, errorCode(error), { 404: 'ddTranslationHealth_importNotFound', 409: 'ddTranslationHealth_alreadyUndone', 500: 'ddTranslationHealth_undoFailed' }),
        );
        // A conflict means the import was undone meanwhile: show its current state.
        if (response?.status === 409) void this.reload();
      }
    } catch {
      this._message = this.localize.term('ddTranslationHealth_loadFailed');
    }
    this._undoing = false;
  }

  #date(utc: string) {
    return this.localize.date(utc, { dateStyle: 'medium', timeStyle: 'short' });
  }

  override render() {
    return html`<uui-box headline=${this.localize.term('ddTranslationHealth_history')}>
      ${this._failed ? html`<p class="error">${this.localize.term('ddTranslationHealth_loadFailed')}</p>` : nothing} ${this.#renderList()}
      ${this._open ? this.#renderDetail(this._open) : nothing}
    </uui-box>`;
  }

  #renderList() {
    if (this._loading && this._imports.length === 0) return html`<uui-loader></uui-loader>`;
    if (this._imports.length === 0) return html`<p class="muted">${this.localize.term('ddTranslationHealth_noHistory')}</p>`;
    const pages = Math.max(1, Math.ceil(this._total / PAGE_SIZE));
    return html`<div class="table-wrap">
        <uui-table>
          <uui-table-head>
            <uui-table-head-cell>${this.localize.term('ddTranslationHealth_date')}</uui-table-head-cell>
            <uui-table-head-cell>${this.localize.term('ddTranslationHealth_user')}</uui-table-head-cell>
            <uui-table-head-cell>${this.localize.term('ddTranslationHealth_file')}</uui-table-head-cell>
            <uui-table-head-cell>${this.localize.term('ddTranslationHealth_applied')}</uui-table-head-cell>
            <uui-table-head-cell>${this.localize.term('ddTranslationHealth_state')}</uui-table-head-cell>
            <uui-table-head-cell></uui-table-head-cell>
          </uui-table-head>
          ${repeat(
            this._imports,
            (i) => i.key,
            (i) => html`<uui-table-row>
              <uui-table-cell>${this.#date(i.createdUtc)}</uui-table-cell>
              <uui-table-cell>${i.userName}</uui-table-cell>
              <uui-table-cell class="break">${i.fileName} <span class="muted">(${i.format})</span></uui-table-cell>
              <uui-table-cell>${i.applied}${i.skipped > 0 ? html` <span class="muted">/ ${this.localize.term('ddTranslationHealth_skippedCount', i.skipped)}</span>` : nothing}</uui-table-cell>
              <uui-table-cell>
                <uui-tag color=${i.state === 'Undone' ? 'default' : 'positive'}>${this.localize.term(`ddTranslationHealth_importState${i.state}`)}</uui-tag>
              </uui-table-cell>
              <uui-table-cell>
                <uui-button look="secondary" compact label=${this.localize.term('ddTranslationHealth_details')} @click=${() => void this.#openDetail(i)}></uui-button>
              </uui-table-cell>
            </uui-table-row>`,
          )}
        </uui-table>
      </div>
      ${pages > 1
        ? html`<uui-pagination
            .total=${pages}
            .current=${this._page}
            @change=${(e: UUIPaginationEvent) => {
              this._page = e.target.current;
              void this.reload();
            }}></uui-pagination>`
        : nothing}`;
  }

  #renderDetail(item: ImportModel) {
    const detail = this._detail;
    const pages = detail ? Math.max(1, Math.ceil(detail.totalRows / PAGE_SIZE)) : 1;
    return html`<div class="detail">
      <h4>${item.fileName} — ${this.#date(item.createdUtc)}</h4>
      <p class="hint">
        ${item.userName} · ${this.localize.term(item.overwrite ? 'ddTranslationHealth_overwrite' : 'ddTranslationHealth_noOverwrite')} ·
        ${this.localize.term(item.createKeys ? 'ddTranslationHealth_createKeys' : 'ddTranslationHealth_noCreateKeys')}
        ${item.state === 'Undone' && item.undoneUtc
          ? html` · ${this.localize.term('ddTranslationHealth_undoneBy', item.undoneBy ?? '', this.#date(item.undoneUtc))}`
          : nothing}
      </p>
      ${this._message ? html`<p class="error">${this._message}</p>` : nothing}
      ${this._undone?.key === item.key ? this.#renderUndoResult(this._undone.result) : nothing}
      <div class="actions">
        <uui-button
          look="primary"
          color="danger"
          label=${this.localize.term('ddTranslationHealth_undo')}
          ?disabled=${this._undoing || item.state !== 'Applied'}
          .state=${this._undoing ? 'waiting' : undefined}
          @click=${() => void this.#undo(item)}></uui-button>
        <uui-button look="secondary" label=${this.localize.term('ddTranslationHealth_close')} @click=${() => (this._open = undefined)}></uui-button>
      </div>
      ${this._detailLoading && !detail
        ? html`<uui-loader></uui-loader>`
        : detail
          ? html`<div class="table-wrap">
                <uui-table>
                  <uui-table-head>
                    <uui-table-head-cell>${this.localize.term('ddTranslationHealth_key')}</uui-table-head-cell>
                    <uui-table-head-cell>${this.localize.term('ddTranslationHealth_language')}</uui-table-head-cell>
                    <uui-table-head-cell>${this.localize.term('ddTranslationHealth_oldValue')}</uui-table-head-cell>
                    <uui-table-head-cell>${this.localize.term('ddTranslationHealth_newValue')}</uui-table-head-cell>
                  </uui-table-head>
                  ${detail.rows.map(
                    (r) => html`<uui-table-row>
                      <uui-table-cell class="break"><code>${r.key}</code> ${r.keyCreated ? html`<uui-tag color="positive">${this.localize.term('ddTranslationHealth_outcomeNewKey')}</uui-tag>` : nothing}</uui-table-cell>
                      <uui-table-cell>${r.culture}</uui-table-cell>
                      <uui-table-cell class="break">${r.oldValue || html`<span class="muted">—</span>`}</uui-table-cell>
                      <uui-table-cell class="break">${r.newValue}</uui-table-cell>
                    </uui-table-row>`,
                  )}
                </uui-table>
              </div>
              ${pages > 1
                ? html`<uui-pagination
                    .total=${pages}
                    .current=${this._detailPage}
                    @change=${(e: UUIPaginationEvent) => void this.#openDetail(item, e.target.current)}></uui-pagination>`
                : nothing}`
          : nothing}
    </div>`;
  }

  #renderUndoResult(result: UndoResponse) {
    return html`<div class="result">
      <div class="tags">
        <uui-tag color="positive">${this.localize.term('ddTranslationHealth_restored')}: ${result.restored}</uui-tag>
        <uui-tag color="positive">${this.localize.term('ddTranslationHealth_keysRemoved')}: ${result.keysRemoved}</uui-tag>
        <uui-tag color=${result.leftAlone.length > 0 ? 'warning' : 'default'}>${this.localize.term('ddTranslationHealth_leftAlone')}: ${result.leftAlone.length}</uui-tag>
      </div>
      ${result.leftAlone.length > 0
        ? html`<ul>
            ${result.leftAlone.map(
              (r) => html`<li>
                <code>${r.key}</code> ${r.culture} —
                ${this.localize.term(r.keyCreated ? 'ddTranslationHealth_leftAloneKey' : 'ddTranslationHealth_leftAloneValue')}
              </li>`,
            )}
          </ul>`
        : nothing}
    </div>`;
  }

  static override styles = [
    sharedStyles,
    css`
      :host {
        padding: 0;
      }
      .detail {
        margin-top: var(--uui-size-space-6);
        border-top: 1px solid var(--uui-color-border);
        padding-top: var(--uui-size-space-4);
      }
      h4 {
        margin: 0;
      }
      uui-table-cell.break {
        max-width: 40ch;
      }
    `,
  ];
}

/** The "Import and export" dashboard: export, import with test run, and history. */
@customElement('dd-translation-health-files')
export class DdTranslationHealthFilesElement extends UmbLitElement {
  #history?: DdTranslationHealthHistoryElement;

  override render() {
    return html`
      <dd-translation-health-export></dd-translation-health-export>
      <dd-translation-health-import
        @dd-translation-health-imports-changed=${() => {
          this.#history ??= this.shadowRoot?.querySelector<DdTranslationHealthHistoryElement>('dd-translation-health-history') ?? undefined;
          void this.#history?.reload();
        }}></dd-translation-health-import>
      <dd-translation-health-history></dd-translation-health-history>
    `;
  }

  static override styles = [
    sharedStyles,
    css`
      dd-translation-health-export,
      dd-translation-health-import,
      dd-translation-health-history {
        display: block;
        margin-bottom: var(--uui-size-layout-1);
      }
    `,
  ];
}

export default DdTranslationHealthFilesElement;

declare global {
  interface HTMLElementTagNameMap {
    'dd-translation-health-files': DdTranslationHealthFilesElement;
    'dd-translation-health-export': DdTranslationHealthExportElement;
    'dd-translation-health-import': DdTranslationHealthImportElement;
    'dd-translation-health-history': DdTranslationHealthHistoryElement;
  }
}

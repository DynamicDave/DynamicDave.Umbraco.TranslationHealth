import { css, html, customElement, state, repeat, nothing } from '@umbraco-cms/backoffice/external/lit';
import { UmbLitElement } from '@umbraco-cms/backoffice/lit-element';
import type { UUIBooleanInputEvent, UUIInputEvent, UUIPaginationEvent, UUISelectEvent } from '@umbraco-cms/backoffice/external/uui';
import { TranslationHealthService } from './api/index.js';
import type {
  CellFilter,
  DocumentTypeModel,
  JobStateModel,
  LanguageModel,
  LanguageTotalModel,
  OverviewRowModel,
  SiteModel,
  StatusModel,
  VariantCellModel,
} from './api/index.js';
import {
  MAX_POLL_MS,
  PAGE_SIZE,
  POLL_MS,
  daysSince,
  documentHref,
  errorCode,
  errorTerm,
  failureTemplate,
  fileNameFrom,
  isJob,
  notify,
  progressTemplate,
  saveBlob,
  sharedStyles,
  statusColor,
  statusTerm,
} from './shared.js';

const STATUS_FILTERS: Array<{ value: CellFilter; term: string }> = [
  { value: 'AnyIssue', term: 'ddTranslationHealth_anyIssue' },
  { value: 'Missing', term: 'ddTranslationHealth_statusMissing' },
  { value: 'Draft', term: 'ddTranslationHealth_statusDraft' },
  { value: 'PendingChanges', term: 'ddTranslationHealth_statusPendingChanges' },
  { value: 'Outdated', term: 'ddTranslationHealth_outdated' },
];

/** The translation overview (spec 4.4): totals per language, filters, the page table, export and rebuild. */
@customElement('dd-translation-health-pages')
export class DdTranslationHealthPagesElement extends UmbLitElement {
  @state() private _languages: LanguageModel[] = [];
  @state() private _sites: SiteModel[] = [];
  @state() private _types: DocumentTypeModel[] = [];
  @state() private _totals: LanguageTotalModel[] = [];
  @state() private _rows: OverviewRowModel[] = [];
  @state() private _total = 0;
  @state() private _page = 1;
  @state() private _siteRootKey = '';
  @state() private _documentType = '';
  @state() private _culture = '';
  @state() private _status: CellFilter | '' = '';
  @state() private _search = '';
  @state() private _onlyIssues = false;
  @state() private _loading = true;
  @state() private _failed?: 'loadFailed' | 'forbidden';
  @state() private _serverStatus?: StatusModel;
  @state() private _job?: JobStateModel;
  @state() private _message?: string;
  @state() private _rebuilding = false;
  @state() private _exporting = false;

  /** Incremented per request so responses of superseded requests are ignored. */
  #rowsRequest = 0;
  #totalsRequest = 0;
  #jobRequest = 0;
  #pollTimer?: ReturnType<typeof setTimeout>;
  #searchTimer?: ReturnType<typeof setTimeout>;
  #failures = 0;
  #connected = false;

  override connectedCallback() {
    super.connectedCallback();
    this.#connected = true;
    void this.#init();
    void this.#loadJob();
  }

  override disconnectedCallback() {
    super.disconnectedCallback();
    this.#connected = false;
    clearTimeout(this.#pollTimer);
    clearTimeout(this.#searchTimer);
    this.#rowsRequest++;
    this.#totalsRequest++;
    this.#jobRequest++;
  }

  async #init() {
    let failed: 'loadFailed' | 'forbidden' | undefined;
    try {
      const [languages, sites, types, status] = await Promise.all([
        TranslationHealthService.getLanguages({ throwOnError: false }),
        TranslationHealthService.getSites({ throwOnError: false }),
        TranslationHealthService.getDocumentTypes({ throwOnError: false }),
        TranslationHealthService.getStatus({ throwOnError: false }),
      ]);
      if (!this.#connected) return;
      if ([languages, sites, types].some((r) => r.response?.status === 403)) failed = 'forbidden';
      else if (!languages.data || !sites.data || !types.data) failed = 'loadFailed';
      this._languages = languages.data ?? [];
      this._sites = sites.data ?? [];
      this._types = types.data ?? [];
      this._serverStatus = status.data;
    } catch {
      failed = 'loadFailed';
    }
    if (!this.#connected) return;
    this._failed = failed;
    if (failed) {
      this._loading = false;
      return;
    }
    await Promise.all([this.#loadRows(), this.#loadTotals()]);
  }

  #query() {
    return {
      siteRootKey: this._siteRootKey || undefined,
      documentType: this._documentType || undefined,
      culture: this._culture || undefined,
      status: this._status || undefined,
      search: this._search.trim() || undefined,
      onlyIssues: this._onlyIssues || undefined,
    };
  }

  async #loadRows(clamped = false) {
    const requestId = ++this.#rowsRequest;
    this._loading = true;
    let rows: OverviewRowModel[] = [];
    let total = 0;
    let failed = false;
    try {
      const { data } = await TranslationHealthService.getOverview({
        throwOnError: false,
        query: { ...this.#query(), skip: (this._page - 1) * PAGE_SIZE, take: PAGE_SIZE },
      });
      failed = !data;
      rows = data?.items ?? [];
      total = data?.total ?? 0;
    } catch {
      failed = true;
    }
    if (requestId !== this.#rowsRequest || !this.#connected) return; // stale response or detached
    const lastPage = Math.max(1, Math.ceil(total / PAGE_SIZE));
    if (!failed && !clamped && total > 0 && rows.length === 0 && this._page > lastPage) {
      this._page = lastPage;
      void this.#loadRows(true);
      return;
    }
    this._rows = rows;
    this._total = total;
    this._failed = failed ? 'loadFailed' : undefined;
    this._loading = false;
  }

  async #loadTotals() {
    const requestId = ++this.#totalsRequest;
    let totals: LanguageTotalModel[] | undefined;
    try {
      const { siteRootKey, documentType } = this.#query();
      totals = (await TranslationHealthService.getOverviewTotals({ throwOnError: false, query: { siteRootKey, documentType } })).data;
    } catch {
      totals = undefined;
    }
    if (requestId !== this.#totalsRequest || !this.#connected) return;
    if (totals) this._totals = totals;
  }

  async #loadStatus() {
    try {
      const { data } = await TranslationHealthService.getStatus({ throwOnError: false });
      if (this.#connected && data) this._serverStatus = data;
    } catch {
      // The notice just stays as it was.
    }
  }

  /** Loads the current job and keeps polling every 2 s while it runs; reloads the data when it has finished. */
  async #loadJob() {
    const requestId = ++this.#jobRequest;
    clearTimeout(this.#pollTimer);
    let job: JobStateModel | undefined;
    let failed = false;
    try {
      const { data, error, response } = await TranslationHealthService.getCurrentJob({ throwOnError: false });
      failed = error !== undefined && response?.status !== 204;
      job = isJob(data) ? data : undefined; // 204: no job yet
    } catch {
      failed = true;
    }
    if (requestId !== this.#jobRequest || !this.#connected) return;
    const wasRunning = this._job?.status === 'Running';
    if (!failed) {
      this._job = job;
      this.#failures = 0;
    }
    if (failed) {
      this.#failures++;
      this.#pollTimer = setTimeout(() => void this.#loadJob(), Math.min(POLL_MS * 2 ** (this.#failures - 1), MAX_POLL_MS));
    } else if (job?.status === 'Running') {
      this.#pollTimer = setTimeout(() => void this.#loadJob(), POLL_MS);
    } else if (wasRunning) {
      void this.#loadRows();
      void this.#loadTotals();
      void this.#loadStatus();
    }
  }

  async #rebuild() {
    this._rebuilding = true;
    this._message = undefined;
    try {
      const { data, error } = await TranslationHealthService.rebuildOverview({ throwOnError: false });
      if (isJob(data)) {
        this._job = data;
        clearTimeout(this.#pollTimer);
        this.#pollTimer = setTimeout(() => void this.#loadJob(), POLL_MS);
      } else if (isJob(error)) {
        // 409: a rebuild is already running (started by someone else, or earlier).
        this._job = error;
        this._message = this.localize.term('ddTranslationHealth_jobBusy', error.userName);
        clearTimeout(this.#pollTimer);
        this.#pollTimer = setTimeout(() => void this.#loadJob(), POLL_MS);
      } else {
        this._message = this.localize.term(errorTerm(errorCode(error)));
      }
    } catch {
      this._message = this.localize.term('ddTranslationHealth_loadFailed');
    }
    this._rebuilding = false;
  }

  async #export() {
    this._exporting = true;
    this._message = undefined;
    let blob: Blob | undefined;
    let response: Response | undefined;
    try {
      const result = await TranslationHealthService.exportOverview({ throwOnError: false, query: this.#query(), parseAs: 'blob' });
      blob = result.data instanceof Blob ? result.data : undefined;
      response = result.response;
    } catch {
      blob = undefined;
    }
    this._exporting = false;
    if (!blob) {
      void notify(this, 'danger', this.localize.term('ddTranslationHealth_exportFailed'));
      return;
    }
    saveBlob(blob, fileNameFrom(response, 'translation-overview.csv'));
  }

  #filterChanged(apply: () => void) {
    apply();
    this._page = 1;
    void this.#loadRows();
    void this.#loadTotals();
  }

  #searchInput(e: UUIInputEvent) {
    const value = String(e.target.value ?? '');
    clearTimeout(this.#searchTimer);
    this.#searchTimer = setTimeout(() => this.#filterChanged(() => (this._search = value)), 300);
  }

  #languageName(culture: string) {
    return this._languages.find((l) => l.culture === culture)?.name ?? culture;
  }

  override render() {
    if (this._failed && this._rows.length === 0 && !this._loading) {
      return html`<uui-box headline=${this.localize.term('ddTranslationHealth_pagesTitle')}>
        <p>${this.localize.term(`ddTranslationHealth_${this._failed}`)}</p>
      </uui-box>`;
    }
    if (this._languages.length === 0 && this._loading) return html`<uui-loader></uui-loader>`;
    return html`
      ${this.#renderNotices()}
      <uui-box headline=${this.localize.term('ddTranslationHealth_pagesTitle')}>
        ${this.#renderToolbar()} ${this.#renderTotals()} ${this.#renderFilters()}
        ${this._message ? html`<p class="error">${this._message}</p>` : nothing} ${this.#renderTable()}
      </uui-box>
    `;
  }

  #renderNotices() {
    const selected = this._siteRootKey;
    const noSource = this._sites.filter((s) => s.missingDefaultCulture && (!selected || s.rootKey === selected));
    return html`
      ${this._serverStatus?.lastIndexError
        ? html`<div class="notice danger"><uui-icon name="icon-alert"></uui-icon>${this.localize.term('ddTranslationHealth_indexError')}</div>`
        : nothing}
      ${noSource.map(
        (s) => html`<div class="notice">
          <uui-icon name="icon-info"></uui-icon><span><strong>${s.name}</strong>: ${this.localize.term('ddTranslationHealth_noSourceOnSite')}</span>
        </div>`,
      )}
    `;
  }

  #renderToolbar() {
    const running = this._job?.status === 'Running';
    const lastRebuild = this._serverStatus?.lastRebuildUtc;
    return html`
      <div class="toolbar">
        <div class="toolbar-info">
          <p class="hint">${this.localize.term('ddTranslationHealth_rebuildHint')}</p>
          ${lastRebuild
            ? html`<p class="hint">${this.localize.term('ddTranslationHealth_lastRebuild', this.localize.date(lastRebuild, { dateStyle: 'medium', timeStyle: 'short' }))}</p>`
            : nothing}
        </div>
        <uui-button
          look="secondary"
          label=${this.localize.term('ddTranslationHealth_rebuild')}
          ?disabled=${running || this._rebuilding}
          .state=${running || this._rebuilding ? 'waiting' : undefined}
          @click=${this.#rebuild}></uui-button>
        <uui-button
          look="secondary"
          label=${this.localize.term('ddTranslationHealth_exportCsv')}
          ?disabled=${this._exporting}
          .state=${this._exporting ? 'waiting' : undefined}
          @click=${this.#export}></uui-button>
      </div>
      ${this._job?.status === 'Running' ? progressTemplate(this.localize, this._job) : this._job ? this.#renderJobEnd(this._job) : nothing}
    `;
  }

  #renderJobEnd(job: JobStateModel) {
    return failureTemplate(this.localize, job);
  }

  #renderTotals() {
    if (this._totals.length === 0) return nothing;
    const defaultCulture = this._languages.find((l) => l.isDefault)?.culture;
    const ordered = [...this._totals].sort((a, b) => Number(b.culture === defaultCulture) - Number(a.culture === defaultCulture));
    return html`<div class="cards">
      ${ordered.map((t) => {
        const isDefault = t.culture === defaultCulture;
        const percent = t.pages > 0 ? Math.round((t.complete / t.pages) * 100) : 0;
        return html`<div class="card">
          <div class="card-head">
            <strong>${this.#languageName(t.culture)}</strong>
            ${isDefault ? html`<uui-tag look="secondary">${this.localize.term('ddTranslationHealth_defaultLanguage')}</uui-tag>` : nothing}
          </div>
          <div class="figure">${isDefault ? t.pages : `${percent}%`}</div>
          <div class="muted">
            ${isDefault
              ? this.localize.term('ddTranslationHealth_pagesCount', t.pages)
              : this.localize.term('ddTranslationHealth_completeOf', t.complete, t.pages)}
          </div>
          <div class="tags small">
            ${t.missing > 0 ? html`<uui-tag color="danger">${this.localize.term('ddTranslationHealth_statusMissing')}: ${t.missing}</uui-tag>` : nothing}
            ${t.draft > 0 ? html`<uui-tag>${this.localize.term('ddTranslationHealth_statusDraft')}: ${t.draft}</uui-tag>` : nothing}
            ${t.pendingChanges > 0
              ? html`<uui-tag color="warning">${this.localize.term('ddTranslationHealth_statusPendingChanges')}: ${t.pendingChanges}</uui-tag>`
              : nothing}
            ${!isDefault && t.outdated > 0
              ? html`<uui-tag color="warning">${this.localize.term('ddTranslationHealth_outdated')}: ${t.outdated}</uui-tag>`
              : nothing}
          </div>
        </div>`;
      })}
    </div>`;
  }

  #renderFilters() {
    const option = (value: string, name: string, current: string) => ({ name, value, selected: value === current });
    return html`<div class="filters">
      <label>
        ${this.localize.term('ddTranslationHealth_site')}
        <uui-select
          label=${this.localize.term('ddTranslationHealth_site')}
          .options=${[
            option('', this.localize.term('ddTranslationHealth_allSites'), this._siteRootKey),
            ...this._sites.map((s) => option(s.rootKey, s.name, this._siteRootKey)),
          ]}
          @change=${(e: UUISelectEvent) => this.#filterChanged(() => (this._siteRootKey = String(e.target.value)))}></uui-select>
      </label>
      <label>
        ${this.localize.term('ddTranslationHealth_documentType')}
        <uui-select
          label=${this.localize.term('ddTranslationHealth_documentType')}
          .options=${[
            option('', this.localize.term('ddTranslationHealth_allTypes'), this._documentType),
            ...this._types.map((t) => option(t.alias, t.name, this._documentType)),
          ]}
          @change=${(e: UUISelectEvent) => this.#filterChanged(() => (this._documentType = String(e.target.value)))}></uui-select>
      </label>
      <label>
        ${this.localize.term('ddTranslationHealth_language')}
        <uui-select
          label=${this.localize.term('ddTranslationHealth_language')}
          .options=${[
            option('', this.localize.term('ddTranslationHealth_allLanguages'), this._culture),
            ...this._languages.map((l) => option(l.culture, l.name, this._culture)),
          ]}
          @change=${(e: UUISelectEvent) => this.#filterChanged(() => (this._culture = String(e.target.value)))}></uui-select>
      </label>
      <label>
        ${this.localize.term('ddTranslationHealth_status')}
        <uui-select
          label=${this.localize.term('ddTranslationHealth_status')}
          .options=${[
            option('', this.localize.term('ddTranslationHealth_anyStatus'), this._status),
            ...STATUS_FILTERS.map((f) => option(f.value, this.localize.term(f.term), this._status)),
          ]}
          @change=${(e: UUISelectEvent) => this.#filterChanged(() => (this._status = String(e.target.value) as CellFilter | ''))}></uui-select>
      </label>
      <label>
        ${this.localize.term('ddTranslationHealth_search')}
        <uui-input label=${this.localize.term('ddTranslationHealth_search')} .value=${this._search} @input=${this.#searchInput}></uui-input>
      </label>
      <uui-checkbox
        label=${this.localize.term('ddTranslationHealth_onlyIssues')}
        ?checked=${this._onlyIssues}
        @change=${(e: UUIBooleanInputEvent) => this.#filterChanged(() => (this._onlyIssues = e.target.checked))}
        >${this.localize.term('ddTranslationHealth_onlyIssues')}</uui-checkbox
      >
    </div>`;
  }

  #renderTable() {
    if (this._loading && this._rows.length === 0) return html`<uui-loader></uui-loader>`;
    if (this._rows.length === 0) return html`<p class="muted">${this.localize.term('ddTranslationHealth_noResults')}</p>`;
    const pages = Math.max(1, Math.ceil(this._total / PAGE_SIZE));
    // The default language first, then core's language order (the API sends the languages that way).
    const cultures = this._languages.map((l) => l.culture);
    return html`
      <div class="table-wrap">
        <uui-table>
          <uui-table-head>
            <uui-table-head-cell>${this.localize.term('ddTranslationHealth_page')}</uui-table-head-cell>
            ${cultures.map(
              (c) => html`<uui-table-head-cell>
                ${this.#languageName(c)}
                ${this._languages.find((l) => l.culture === c)?.isDefault
                  ? html`<uui-tag look="secondary">${this.localize.term('ddTranslationHealth_defaultLanguage')}</uui-tag>`
                  : nothing}
              </uui-table-head-cell>`,
            )}
          </uui-table-head>
          ${repeat(
            this._rows,
            (r) => r.key,
            (r) => html`<uui-table-row>
              <uui-table-cell>
                <a class="link name" style="padding-left: calc(${Math.max(0, r.level - 1)} * var(--uui-size-space-5))" href=${documentHref(r.key)}>${r.name}</a>
              </uui-table-cell>
              ${cultures.map((c) => html`<uui-table-cell>${this.#renderCell(r, c)}</uui-table-cell>`)}
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
              void this.#loadRows();
            }}></uui-pagination>`
        : nothing}
    `;
  }

  #renderCell(row: OverviewRowModel, culture: string) {
    const cell: VariantCellModel | undefined = row.cells.find((c) => c.culture === culture);
    if (!cell || cell.status === 'NotApplicable') {
      return html`<span class="muted" title=${this.localize.term('ddTranslationHealth_statusNotApplicable')}>—</span>`;
    }
    // The default language is the source: it is never outdated and never estimated.
    const isDefault = this._languages.find((l) => l.culture === culture)?.isDefault === true;
    const outdated = cell.outdated && !isDefault;
    const href = documentHref(row.key, culture);
    if (!outdated) {
      return html`<a class="cell-link" href=${href}
        ><uui-tag color=${statusColor(cell.status)}>${this.localize.term(statusTerm(cell.status))}</uui-tag></a
      >`;
    }
    const days = daysSince(cell.outdatedSinceUtc);
    return html`<a class="cell-link" href=${href}>
      <uui-tag color="warning">${this.localize.term('ddTranslationHealth_outdated')}</uui-tag>
      <span class="sub">${this.localize.term(statusTerm(cell.status))}${days !== undefined ? ` · ${this.localize.term('ddTranslationHealth_outdatedFor', days)}` : ''}</span>
      ${cell.mode === 'Date'
        ? html`<uui-icon name="icon-info" title=${this.localize.term('ddTranslationHealth_estimated')}></uui-icon>`
        : nothing}
    </a>`;
  }

  static override styles = [
    sharedStyles,
    css`
      .toolbar {
        display: flex;
        flex-wrap: wrap;
        align-items: center;
        gap: var(--uui-size-space-4);
      }
      .toolbar-info {
        flex: 1;
        min-width: 240px;
      }
      .toolbar-info p {
        margin: 0;
      }
      .cards {
        display: grid;
        grid-template-columns: repeat(auto-fill, minmax(190px, 1fr));
        gap: var(--uui-size-space-4);
        margin: var(--uui-size-space-5) 0;
      }
      .card {
        border: 1px solid var(--uui-color-border);
        border-radius: var(--uui-border-radius);
        padding: var(--uui-size-space-4);
        background: var(--uui-color-surface);
      }
      .card-head {
        display: flex;
        flex-wrap: wrap;
        align-items: center;
        gap: var(--uui-size-space-2);
      }
      .figure {
        font-size: var(--uui-type-h3-size, 1.5rem);
        font-weight: bold;
      }
      .tags.small {
        margin: var(--uui-size-space-2) 0 0;
      }
      .cell-link {
        display: inline-flex;
        flex-wrap: wrap;
        align-items: center;
        gap: var(--uui-size-space-2);
        color: inherit;
        text-decoration: none;
      }
      .cell-link:hover uui-tag {
        outline: 1px solid var(--uui-color-interactive);
      }
      .sub {
        font-size: var(--uui-type-small-size);
        color: var(--uui-color-text-alt);
      }
      .name {
        display: inline-block;
        font-weight: bold;
      }
    `,
  ];
}

export default DdTranslationHealthPagesElement;

declare global {
  interface HTMLElementTagNameMap {
    'dd-translation-health-pages': DdTranslationHealthPagesElement;
  }
}

import { css, html, customElement, state, repeat, nothing } from '@umbraco-cms/backoffice/external/lit';
import { UmbLitElement } from '@umbraco-cms/backoffice/lit-element';
import type { UUIInputEvent, UUIPaginationEvent, UUISelectEvent } from '@umbraco-cms/backoffice/external/uui';
import { TranslationDictionaryService, TranslationHealthService } from './api/index.js';
import type { LanguageModel, MissingCountModel, MissingItemModel } from './api/index.js';
import { PAGE_SIZE, dictionaryHref, sharedStyles } from './shared.js';

/** The missing translations report (spec 5.1): dictionary items with an empty value, per language. */
@customElement('dd-translation-health-missing')
export class DdTranslationHealthMissingElement extends UmbLitElement {
  @state() private _languages: LanguageModel[] = [];
  @state() private _groups: string[] = [];
  @state() private _counts: MissingCountModel[] = [];
  @state() private _items: MissingItemModel[] = [];
  @state() private _total = 0;
  @state() private _page = 1;
  @state() private _culture = '';
  @state() private _group = '';
  @state() private _search = '';
  @state() private _loading = true;
  @state() private _failed?: 'loadFailed' | 'forbidden';

  #request = 0;
  #searchTimer?: ReturnType<typeof setTimeout>;
  #connected = false;

  override connectedCallback() {
    super.connectedCallback();
    this.#connected = true;
    void this.#init();
  }

  override disconnectedCallback() {
    super.disconnectedCallback();
    this.#connected = false;
    clearTimeout(this.#searchTimer);
    this.#request++;
  }

  async #init() {
    try {
      const [languages, groups] = await Promise.all([
        TranslationHealthService.getLanguages({ throwOnError: false }),
        TranslationDictionaryService.getDictionaryGroups({ throwOnError: false }),
      ]);
      if (!this.#connected) return;
      this._languages = languages.data ?? [];
      this._groups = groups.data ?? [];
    } catch {
      // The report still works without the names and the group list.
    }
    await this.#load();
  }

  async #load(clamped = false) {
    const requestId = ++this.#request;
    this._loading = true;
    let items: MissingItemModel[] = [];
    let counts: MissingCountModel[] = [];
    let total = 0;
    let failed = false;
    let forbidden = false;
    try {
      const { data, response } = await TranslationDictionaryService.getMissingTranslations({
        throwOnError: false,
        query: {
          culture: this._culture || undefined,
          group: this._group || undefined,
          search: this._search.trim() || undefined,
          skip: (this._page - 1) * PAGE_SIZE,
          take: PAGE_SIZE,
        },
      });
      failed = !data;
      forbidden = response?.status === 403;
      items = data?.items ?? [];
      counts = data?.counts ?? [];
      total = data?.total ?? 0;
    } catch {
      failed = true;
    }
    if (requestId !== this.#request || !this.#connected) return; // stale response or detached
    const lastPage = Math.max(1, Math.ceil(total / PAGE_SIZE));
    if (!failed && !clamped && total > 0 && items.length === 0 && this._page > lastPage) {
      this._page = lastPage;
      void this.#load(true);
      return;
    }
    this._items = items;
    // A failed load keeps the cards and filters of the last good one.
    if (!failed) this._counts = counts;
    this._total = total;
    this._failed = forbidden ? 'forbidden' : failed ? 'loadFailed' : undefined;
    this._loading = false;
  }

  #filterChanged(apply: () => void) {
    apply();
    this._page = 1;
    void this.#load();
  }

  #languageName(culture: string) {
    return this._languages.find((l) => l.culture === culture)?.name ?? culture;
  }

  /** Group names with the dot depth as indentation, e.g. "Footer", "  Contact" for "Footer.Contact". */
  #groupOptions() {
    const label = (group: string) => {
      const parts = group.split('.');
      return `${'  '.repeat(parts.length - 1)}${parts[parts.length - 1]}`;
    };
    return [
      { name: this.localize.term('ddTranslationHealth_allGroups'), value: '', selected: this._group === '' },
      ...[...this._groups]
        .sort((a, b) => a.localeCompare(b))
        .map((g) => ({ name: label(g), value: g, selected: g === this._group })),
    ];
  }

  override render() {
    return html`<uui-box headline=${this.localize.term('ddTranslationHealth_missingTitle')}>
      ${this.#renderCounts()} ${this.#renderFilters()} ${this.#renderTable()}
    </uui-box>`;
  }

  #renderCounts() {
    if (this._counts.length === 0) return nothing;
    return html`<div class="cards">
      ${this._counts.map(
        (c) => html`<button
          type="button"
          class="card ${this._culture === c.culture ? 'active' : ''}"
          @click=${() => this.#filterChanged(() => (this._culture = this._culture === c.culture ? '' : c.culture))}>
          <strong>${this.#languageName(c.culture)}</strong>
          <span class="figure ${c.missing > 0 ? 'bad' : ''}">${c.missing}</span>
          <span class="muted">${this.localize.term('ddTranslationHealth_missingCount')}</span>
        </button>`,
      )}
    </div>`;
  }

  #renderFilters() {
    return html`<div class="filters">
      <label>
        ${this.localize.term('ddTranslationHealth_language')}
        <uui-select
          label=${this.localize.term('ddTranslationHealth_language')}
          .options=${[
            { name: this.localize.term('ddTranslationHealth_allLanguages'), value: '', selected: this._culture === '' },
            ...this._languages.map((l) => ({ name: l.name, value: l.culture, selected: l.culture === this._culture })),
          ]}
          @change=${(e: UUISelectEvent) => this.#filterChanged(() => (this._culture = String(e.target.value)))}></uui-select>
      </label>
      <label>
        ${this.localize.term('ddTranslationHealth_group')}
        <uui-select
          label=${this.localize.term('ddTranslationHealth_group')}
          .options=${this.#groupOptions()}
          @change=${(e: UUISelectEvent) => this.#filterChanged(() => (this._group = String(e.target.value)))}></uui-select>
      </label>
      <label>
        ${this.localize.term('ddTranslationHealth_searchKey')}
        <uui-input
          label=${this.localize.term('ddTranslationHealth_searchKey')}
          .value=${this._search}
          @input=${(e: UUIInputEvent) => {
            const value = String(e.target.value ?? '');
            clearTimeout(this.#searchTimer);
            this.#searchTimer = setTimeout(() => this.#filterChanged(() => (this._search = value)), 300);
          }}></uui-input>
      </label>
    </div>`;
  }

  #renderTable() {
    if (this._failed && !this._loading) {
      return html`<p class="error">${this.localize.term(`ddTranslationHealth_${this._failed}`)}</p>
        ${this._failed === 'loadFailed'
          ? html`<uui-button look="secondary" label=${this.localize.term('ddTranslationHealth_retry')} @click=${() => void this.#load()}></uui-button>`
          : nothing}`;
    }
    if (this._loading && this._items.length === 0) return html`<uui-loader></uui-loader>`;
    if (this._items.length === 0) return html`<p class="muted">${this.localize.term('ddTranslationHealth_noMissing')}</p>`;
    const pages = Math.max(1, Math.ceil(this._total / PAGE_SIZE));
    return html`
      <div class="table-wrap">
        <uui-table>
          <uui-table-head>
            <uui-table-head-cell>${this.localize.term('ddTranslationHealth_key')}</uui-table-head-cell>
            <uui-table-head-cell>${this.localize.term('ddTranslationHealth_group')}</uui-table-head-cell>
            <uui-table-head-cell>${this.localize.term('ddTranslationHealth_missingIn')}</uui-table-head-cell>
          </uui-table-head>
          ${repeat(
            this._items,
            (i) => i.id,
            (i) => html`<uui-table-row>
              <uui-table-cell><a class="link break" href=${dictionaryHref(i.id)}>${i.key}</a></uui-table-cell>
              <uui-table-cell class="muted">${i.group ? i.group.split('.').join(' › ') : '—'}</uui-table-cell>
              <uui-table-cell>
                ${i.missingCultures.map((c) => html`<uui-tag color="danger">${this.#languageName(c)}</uui-tag>`)}
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
              void this.#load();
            }}></uui-pagination>`
        : nothing}
    `;
  }

  static override styles = [
    sharedStyles,
    css`
      .cards {
        display: grid;
        grid-template-columns: repeat(auto-fill, minmax(160px, 1fr));
        gap: var(--uui-size-space-4);
        margin-bottom: var(--uui-size-space-5);
      }
      .card {
        display: flex;
        flex-direction: column;
        gap: var(--uui-size-space-1);
        align-items: flex-start;
        font: inherit;
        color: inherit;
        text-align: left;
        cursor: pointer;
        border: 1px solid var(--uui-color-border);
        border-radius: var(--uui-border-radius);
        padding: var(--uui-size-space-4);
        background: var(--uui-color-surface);
      }
      .card.active {
        border-color: var(--uui-color-selected);
        box-shadow: 0 0 0 1px var(--uui-color-selected);
      }
      .figure {
        font-size: var(--uui-type-h3-size, 1.5rem);
        font-weight: bold;
      }
      .figure.bad {
        color: var(--uui-color-danger);
      }
      uui-table-cell uui-tag + uui-tag {
        margin-left: var(--uui-size-space-2);
      }
    `,
  ];
}

export default DdTranslationHealthMissingElement;

declare global {
  interface HTMLElementTagNameMap {
    'dd-translation-health-missing': DdTranslationHealthMissingElement;
  }
}

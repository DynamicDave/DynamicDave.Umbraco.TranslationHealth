import { css, html, customElement, state, repeat, nothing } from '@umbraco-cms/backoffice/external/lit';
import { UmbLitElement } from '@umbraco-cms/backoffice/lit-element';
import { UMB_DOCUMENT_WORKSPACE_CONTEXT } from '@umbraco-cms/backoffice/document';
import { TranslationHealthService } from './api/index.js';
import type { DocumentTranslationModel, DocumentTranslationsResponse } from './api/index.js';
import { daysSince, outdatedAge, documentHref, errorCode, errorTerm, notify, sharedStyles, statusColor, statusTerm } from './shared.js';

/** The "Translations" tab of a document (spec 4.5): one row per language with its state, and "Mark as up to date". */
@customElement('dd-translation-health-document')
export class DdTranslationHealthDocumentElement extends UmbLitElement {
  @state() private _data?: DocumentTranslationsResponse;
  @state() private _error = false;
  @state() private _marking?: string;

  #key?: string;
  /** Incremented per load so out-of-order responses (and responses for a previous document) are ignored. */
  #requestId = 0;
  /** The persisted-data observer fires once on first consume, right after the unique one already loaded. */
  #persistedSeen = false;

  constructor() {
    super();
    this.consumeContext(UMB_DOCUMENT_WORKSPACE_CONTEXT, (ctx) => {
      this.observe(
        ctx?.unique,
        (unique) => {
          if (unique && unique !== this.#key) {
            this.#key = unique;
            this.#persistedSeen = false;
            // Never show the previous document's rows under a new document.
            this._data = undefined;
            this._error = false;
            void this.#load();
          }
        },
        'ddTranslationHealthUnique',
      );
      // Changes after every save and publish; the server updates the rows before it responds.
      this.observe(
        ctx?.persistedData,
        (data) => {
          const first = !this.#persistedSeen;
          this.#persistedSeen = true;
          // The first value is the state that the unique observer has just loaded.
          if (!first && data && data.unique === this.#key) void this.#load();
        },
        'ddTranslationHealthPersisted',
      );
    });
  }

  override disconnectedCallback() {
    super.disconnectedCallback();
    this.#requestId++; // in-flight requests must not update a detached element
  }

  async #load() {
    const key = this.#key;
    if (!key) return;
    const requestId = ++this.#requestId;
    let data: DocumentTranslationsResponse | undefined;
    try {
      const response = await TranslationHealthService.getDocumentTranslations({ throwOnError: false, path: { key } });
      data = response.error !== undefined ? undefined : response.data;
    } catch {
      data = undefined;
    }
    if (requestId !== this.#requestId) return; // stale response
    this._data = data;
    this._error = data === undefined;
  }

  async #mark(row: DocumentTranslationModel) {
    const key = this.#key;
    if (!key) return;
    this._marking = row.culture;
    try {
      const { data, error, response } = await TranslationHealthService.markUpToDate({ throwOnError: false, path: { key, culture: row.culture } });
      if (data && 'translations' in data) {
        this.#requestId++; // an older load must not overwrite the new state
        this._data = data;
      } else {
        const status = response?.status;
        const term = status === 403 ? 'ddTranslationHealth_forbidden' : errorTerm(errorCode(error) ?? (status === 409 ? 'notOutdated' : undefined));
        void notify(this, 'danger', this.localize.term(term));
        void this.#load();
      }
    } catch {
      void notify(this, 'danger', this.localize.term('ddTranslationHealth_loadFailed'));
    }
    this._marking = undefined;
  }

  #date(utc: string | null | undefined) {
    return utc ? this.localize.date(utc, { dateStyle: 'medium', timeStyle: 'short' }) : '—';
  }

  override render() {
    if (this._error) return html`<uui-box><p>${this.localize.term('ddTranslationHealth_loadFailed')}</p></uui-box>`;
    if (!this._data) return html`<uui-loader></uui-loader>`;
    if (!this._data.variesByCulture) {
      return html`<uui-box headline=${this.localize.term('ddTranslationHealth_tabTitle')}>
        <p class="muted">${this.localize.term('ddTranslationHealth_invariantDocument')}</p>
      </uui-box>`;
    }
    const key = this.#key ?? '';
    return html`<uui-box headline=${this.localize.term('ddTranslationHealth_tabTitle')}>
      <div class="table-wrap">
        <uui-table>
          <uui-table-head>
            <uui-table-head-cell>${this.localize.term('ddTranslationHealth_language')}</uui-table-head-cell>
            <uui-table-head-cell>${this.localize.term('ddTranslationHealth_status')}</uui-table-head-cell>
            <uui-table-head-cell>${this.localize.term('ddTranslationHealth_lastChanged')}</uui-table-head-cell>
            <uui-table-head-cell>${this.localize.term('ddTranslationHealth_lastPublished')}</uui-table-head-cell>
            <uui-table-head-cell></uui-table-head-cell>
          </uui-table-head>
          ${repeat(
            this._data.translations,
            (t) => t.culture,
            (t) => html`<uui-table-row>
              <uui-table-cell>
                <a class="link" href=${documentHref(key, t.culture)}>${t.languageName}</a>
                ${t.isDefault ? html`<uui-tag look="secondary">${this.localize.term('ddTranslationHealth_defaultLanguage')}</uui-tag>` : nothing}
              </uui-table-cell>
              <uui-table-cell>${this.#renderStatus(t)}</uui-table-cell>
              <uui-table-cell>${t.status === 'NotApplicable' ? '—' : this.#date(t.updateDateUtc)}</uui-table-cell>
              <uui-table-cell>${t.status === 'NotApplicable' ? '—' : this.#date(t.publishDateUtc)}</uui-table-cell>
              <uui-table-cell>
                ${t.canMarkUpToDate && t.outdated
                  ? html`<uui-button
                      look="secondary"
                      compact
                      label=${this.localize.term('ddTranslationHealth_markUpToDate')}
                      ?disabled=${this._marking !== undefined}
                      .state=${this._marking === t.culture ? 'waiting' : undefined}
                      @click=${() => void this.#mark(t)}></uui-button>`
                  : nothing}
              </uui-table-cell>
            </uui-table-row>`,
          )}
        </uui-table>
      </div>
    </uui-box>`;
  }

  #renderStatus(t: DocumentTranslationModel) {
    // The default language is the source: never outdated, never estimated.
    const outdated = t.outdated && !t.isDefault;
    const days = outdated ? daysSince(t.outdatedSinceUtc) : undefined;
    const marked =
      !t.isDefault && !outdated && t.baselineSource === 'MarkedUpToDate' && t.markedUtc
        ? html`<div class="sub">${this.localize.term('ddTranslationHealth_markedBy', t.markedByName ?? '', this.#date(t.markedUtc))}</div>`
        : nothing;
    if (!outdated) {
      return html`<uui-tag color=${statusColor(t.status)}>${this.localize.term(statusTerm(t.status))}</uui-tag>${marked}`;
    }
    return html`<uui-tag color="warning">${this.localize.term('ddTranslationHealth_outdated')}</uui-tag>
      <span class="sub">${this.localize.term(statusTerm(t.status))}${days !== undefined ? ` · ${outdatedAge(this.localize, days)}` : ''}</span>
      ${t.mode === 'Date' ? html`<uui-icon name="icon-info" title=${this.localize.term('ddTranslationHealth_estimated')}></uui-icon>` : nothing}`;
  }

  static override styles = [
    sharedStyles,
    css`
      .sub {
        font-size: var(--uui-type-small-size);
        color: var(--uui-color-text-alt);
      }
      uui-table-cell uui-tag + uui-tag {
        margin-left: var(--uui-size-space-2);
      }
    `,
  ];
}

export default DdTranslationHealthDocumentElement;

declare global {
  interface HTMLElementTagNameMap {
    'dd-translation-health-document': DdTranslationHealthDocumentElement;
  }
}

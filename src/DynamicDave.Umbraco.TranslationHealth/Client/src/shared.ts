import { css, html, nothing } from '@umbraco-cms/backoffice/external/lit';
import type { UmbLocalizationController } from '@umbraco-cms/backoffice/localization-api';
import type { UmbLitElement } from '@umbraco-cms/backoffice/lit-element';
import { UMB_NOTIFICATION_CONTEXT } from '@umbraco-cms/backoffice/notification';
import type { CellStatus, ImportErrorReason, ImportOutcome, JobStateModel } from './api/index.js';

export const PAGE_SIZE = 25;
export const POLL_MS = 2000;
export const MAX_POLL_MS = 10_000;

/** The backoffice route of a document, optionally opened in one language. */
export const documentHref = (key: string, culture?: string) =>
  `/umbraco/section/content/workspace/document/edit/${key}${culture ? `/${culture}` : ''}`;

/** The backoffice route of core's dictionary editor. */
export const dictionaryHref = (id: string) => `/umbraco/section/translation/workspace/dictionary/edit/${id}`;

/** A JobStateModel, as opposed to an empty body (204) or an error body. */
export const isJob = (value: unknown): value is JobStateModel =>
  typeof value === 'object' && value !== null && 'id' in value && 'status' in value;

/** The stable error code of an API error body (`{ error: "planExpired" }`); undefined for ProblemDetails or empty bodies. */
export function errorCode(error: unknown): string | undefined {
  if (typeof error !== 'object' || error === null || !('error' in error)) return undefined;
  const code = (error as { error: unknown }).error;
  return typeof code === 'string' && code.length > 0 ? code : undefined;
}

/** API error codes that have a localized message ("ddTranslationHealth_<code>"). */
const ERROR_CODES = new Set([
  'invalidPaging',
  'invalidFilter',
  'invalidCulture',
  'invalidFormat',
  'invalidSelection',
  'planExpired',
  'importNotFound',
  'alreadyUndone',
  'documentNotFound',
  'languageNotFound',
  'notOutdated',
  'languageNotAllowed',
  'applyFailed',
  'undoFailed',
  'indexFailed',
]);

/** Import file errors ("ddTranslationHealth_fileError<code>"). */
const FILE_ERRORS = new Set(['Unreadable', 'MissingKeyColumn', 'TooLarge', 'TooManyRows', 'UnknownTargetLanguage']);

/** The localized text key of an error code; "could not load" for unknown codes and ProblemDetails. */
export function errorTerm(code: string | null | undefined) {
  if (code && ERROR_CODES.has(code)) return `ddTranslationHealth_${code}`;
  if (code && FILE_ERRORS.has(code)) return `ddTranslationHealth_fileError${code}`;
  if (code === 'Cancelled' || code === 'Unexpected') return `ddTranslationHealth_jobError${code}`;
  return 'ddTranslationHealth_loadFailed';
}

export const statusTerm = (status: CellStatus) => `ddTranslationHealth_status${status}`;
export const outcomeTerm = (outcome: ImportOutcome) => `ddTranslationHealth_outcome${outcome}`;
export const reasonTerm = (reason: ImportErrorReason) => `ddTranslationHealth_reason${reason}`;

export function statusColor(status: CellStatus): 'positive' | 'warning' | 'danger' | 'default' {
  switch (status) {
    case 'Published':
      return 'positive';
    case 'PendingChanges':
      return 'warning';
    case 'Missing':
      return 'danger';
    default:
      return 'default';
  }
}

export function outcomeColor(outcome: ImportOutcome): 'positive' | 'warning' | 'danger' | 'default' {
  if (outcome === 'NewValue' || outcome === 'NewKey') return 'positive';
  if (outcome === 'Overwrite') return 'warning';
  return outcome === 'Error' ? 'danger' : 'default';
}

/** Whole days between an ISO date and now; never negative. */
export function daysSince(utc: string | null | undefined, now = Date.now()) {
  if (!utc) return undefined;
  const time = Date.parse(utc);
  return Number.isNaN(time) ? undefined : Math.max(0, Math.floor((now - time) / 86_400_000));
}

/** "Outdated today", "Outdated 1 day" or "Outdated N days". */
export function outdatedAge(localize: UmbLocalizationController, days: number) {
  if (days < 1) return localize.term('ddTranslationHealth_outdatedToday');
  return days === 1 ? localize.term('ddTranslationHealth_outdatedOneDay') : localize.term('ddTranslationHealth_outdatedFor', days);
}

/** The file name from a Content-Disposition header, or the fallback. */
export function fileNameFrom(response: Response | undefined, fallback: string) {
  const header = response?.headers.get('Content-Disposition') ?? '';
  const encoded = /filename\*=UTF-8''([^;]+)/i.exec(header);
  if (encoded) {
    try {
      return decodeURIComponent(encoded[1]);
    } catch {
      // Fall through to the plain file name.
    }
  }
  const plain = /filename="?([^";]+)"?/i.exec(header);
  return plain ? plain[1] : fallback;
}

/** Saves a blob through a temporary link; the object URL is revoked after the browser has started the download. */
export function saveBlob(blob: Blob, fileName: string) {
  const url = URL.createObjectURL(blob);
  const link = document.createElement('a');
  link.href = url;
  link.download = fileName;
  document.body.appendChild(link);
  link.click();
  link.remove();
  setTimeout(() => URL.revokeObjectURL(url), 10_000);
}

/** Shows a notification in the backoffice (a toast). */
export async function notify(host: UmbLitElement, color: 'positive' | 'danger' | 'warning', message: string) {
  const context = await host.getContext(UMB_NOTIFICATION_CONTEXT);
  context?.peek(color, { data: { message } });
}

/** The progress line and bar of a running job. */
export function progressTemplate(localize: UmbLocalizationController, job: JobStateModel) {
  const progress = job.total > 0 ? Math.round((job.done / job.total) * 100) : 0;
  const label = localize.term('ddTranslationHealth_jobStatusRunning');
  return html`<div class="progress">
    <span>${job.total > 0 ? `${label} ${job.done} / ${job.total}` : label}</span>
    ${job.total > 0 ? html`<uui-loader-bar progress=${progress}></uui-loader-bar>` : html`<uui-loader-bar></uui-loader-bar>`}
  </div>`;
}

/** The status of a job that has ended without success. */
export function failureTemplate(localize: UmbLocalizationController, job: JobStateModel) {
  if (job.status !== 'Failed' && job.status !== 'Aborted') return nothing;
  return html`<p class="error">
    <uui-tag color="danger">${localize.term(`ddTranslationHealth_jobStatus${job.status}`)}</uui-tag>
    ${job.error ? localize.term(errorTerm(job.error)) : nothing}
  </p>`;
}

export const sharedStyles = css`
  :host {
    display: block;
    padding: var(--uui-size-layout-1);
  }
  uui-box + uui-box {
    margin-top: var(--uui-size-layout-1);
  }
  .hint {
    color: var(--uui-color-text-alt);
    font-size: var(--uui-type-small-size);
    margin: var(--uui-size-space-1) 0 0;
  }
  .muted {
    color: var(--uui-color-text-alt);
  }
  .error {
    color: var(--uui-color-danger);
  }
  .progress {
    display: flex;
    flex-direction: column;
    gap: var(--uui-size-space-2);
    margin: var(--uui-size-space-4) 0;
  }
  .actions {
    display: flex;
    flex-wrap: wrap;
    align-items: center;
    gap: var(--uui-size-space-4);
    margin-top: var(--uui-size-space-4);
  }
  .filters {
    display: flex;
    flex-wrap: wrap;
    align-items: end;
    gap: var(--uui-size-space-4);
    margin-bottom: var(--uui-size-space-4);
  }
  .filters label {
    display: flex;
    flex-direction: column;
    gap: var(--uui-size-space-1);
    font-size: var(--uui-type-small-size);
  }
  .filters uui-select {
    min-width: 180px;
  }
  .tags {
    display: flex;
    flex-wrap: wrap;
    gap: var(--uui-size-space-2);
    margin: var(--uui-size-space-4) 0;
  }
  .filter {
    padding: 0;
    border: 0;
    background: none;
    font: inherit;
    cursor: pointer;
    border-radius: var(--uui-border-radius);
  }
  .filter.active uui-tag {
    box-shadow: 0 0 0 2px var(--uui-color-selected);
  }
  .notice {
    display: flex;
    gap: var(--uui-size-space-3);
    align-items: center;
    padding: var(--uui-size-space-3) var(--uui-size-space-4);
    margin-bottom: var(--uui-size-space-4);
    border-radius: var(--uui-border-radius);
    background: var(--uui-color-warning);
    color: var(--uui-color-warning-contrast);
  }
  .notice.danger {
    background: var(--uui-color-danger);
    color: var(--uui-color-danger-contrast);
  }
  .table-wrap {
    overflow-x: auto;
  }
  .table-wrap uui-table {
    width: max-content;
    min-width: 100%;
  }
  uui-table-cell uui-tag {
    white-space: nowrap;
  }
  .link {
    color: var(--uui-color-interactive);
    text-decoration: none;
  }
  .link:hover {
    text-decoration: underline;
  }
  uui-pagination {
    display: block;
    margin-top: var(--uui-size-space-4);
  }
  .break {
    overflow-wrap: anywhere;
  }
`;

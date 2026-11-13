import { UmbConditionBase } from '@umbraco-cms/backoffice/extension-registry';
import type { UmbConditionConfigBase, UmbConditionControllerArguments, UmbExtensionCondition } from '@umbraco-cms/backoffice/extension-api';
import type { UmbControllerHost } from '@umbraco-cms/backoffice/controller-api';
import { UMB_DOCUMENT_WORKSPACE_CONTEXT } from '@umbraco-cms/backoffice/document';

/**
 * Permits the "Translations" tab only for culture-variant documents. Observes the document workspace's
 * `variesByCulture`; a workspace context without that observable (an older backoffice) permits the tab, which then
 * asks the API and says so for an invariant document.
 */
export class DdTranslationHealthVariesByCultureCondition extends UmbConditionBase<UmbConditionConfigBase> implements UmbExtensionCondition {
  constructor(host: UmbControllerHost, args: UmbConditionControllerArguments<UmbConditionConfigBase>) {
    super(host, args);
    this.consumeContext(UMB_DOCUMENT_WORKSPACE_CONTEXT, (context) => {
      if (!context) return;
      if (!context.variesByCulture) {
        this.permitted = true;
        return;
      }
      this.observe(
        context.variesByCulture,
        (value) => {
          // undefined while the document type is still loading: the tab stays hidden until it is known.
          if (value !== undefined) this.permitted = value === true;
        },
        'ddTranslationHealthVariesByCulture',
      );
    });
  }
}

export { DdTranslationHealthVariesByCultureCondition as api };

import { api, type Provider } from '@/lib/api';

/**
 * Registering an on-device orchestrator model as a report-generation provider.
 *
 * The backend has had every piece of this for a while — `LlamaCppProvider` is a
 * registered `IAiProviderAdapter`, the llama.cpp runtime is provisioned with the model,
 * and `LlamaServerProcess` starts it on demand — but nothing ever created the
 * `ProviderConfig` row that makes it selectable. The provider admin screen that could
 * create one lives in the `(web)` route group, which `build:desktop` stages out, so the
 * radiologist actually running the model had no way to reach it. This closes that gap
 * from the desktop model manager.
 */

/** Adapter id of the llama.cpp HTTP adapter (`LlamaCppProvider.AdapterId`). */
export const LOCAL_LLAMA_ADAPTER = 'llama-cpp';

/** Loopback default (`LlamaServerProcess.BaseUrl`). PHI must never leave the device. */
export const LOCAL_LLAMA_ENDPOINT = 'http://127.0.0.1:8080';

/** `ProviderComplianceClass.LocalOnly`. */
const COMPLIANCE_LOCAL_ONLY = 4;

/**
 * Deliberately the neutral default rather than a preferred one.
 *
 * `resolveDefaultProvider` picks the LOWEST priority number as a tenant's default, and
 * auto-routing considers every enabled row. Registering an on-device model is a personal
 * choice by one radiologist at one workstation; it must not silently redirect a
 * colleague's report generation to a model their machine may not even have. The provider
 * becomes *selectable*, and per-radiologist stickiness is handled by `providerPref`.
 */
const NEUTRAL_PRIORITY = 100;

/** The registered provider row for a given on-device model, if one already exists. */
export function findOnDeviceProvider(
  providers: readonly Provider[],
  modelId: string,
): Provider | undefined {
  return providers.find((p) => p.adapter === LOCAL_LLAMA_ADAPTER && p.model === modelId);
}

export type EnsureProviderResult =
  | { status: 'created'; provider: Provider }
  | { status: 'enabled'; provider: Provider }
  | { status: 'already'; provider: Provider }
  | { status: 'forbidden' };

/**
 * Make an on-device model selectable in the report-generation provider picker,
 * creating the tenant provider row or re-enabling an existing one.
 *
 * Every field is sent explicitly. `SaveProviderDto` leaves `Compliance`, `Enabled` and
 * `Priority` without defaults, so an omitted field would deserialize to `0` — which
 * would mean Blocked, disabled, and top-priority respectively. Two of those are merely
 * broken; the third would quietly make this model the tenant's default provider.
 */
export async function ensureOnDeviceProvider(
  modelId: string,
  displayName: string,
): Promise<EnsureProviderResult> {
  const providers = await api.providers.list();
  const existing = findOnDeviceProvider(providers, modelId);

  if (existing?.enabled) return { status: 'already', provider: existing };

  try {
    await api.providers.save({
      id: existing?.id ?? null,
      name: existing?.name ?? displayName,
      adapter: LOCAL_LLAMA_ADAPTER,
      model: modelId,
      endpointUrl: existing?.endpointUrl || LOCAL_LLAMA_ENDPOINT,
      apiKeySecretRef: '',
      compliance: COMPLIANCE_LOCAL_ONLY,
      enabled: true,
      priority: existing?.priority ?? NEUTRAL_PRIORITY,
    });
  } catch (e) {
    // Creating a tenant provider is an admin action. A radiologist without
    // ProvidersManage gets a clear explanation instead of a dead button.
    if ((e as { status?: number })?.status === 403) return { status: 'forbidden' };
    throw e;
  }

  const after = await api.providers.list();
  const saved = findOnDeviceProvider(after, modelId);
  if (!saved) throw new Error('The provider was saved but did not come back in the list.');
  return { status: existing ? 'enabled' : 'created', provider: saved };
}

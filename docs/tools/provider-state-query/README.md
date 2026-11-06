# Provider State Query

**Tool ID:** `providers.readiness` · **Section:** Providers

## Purpose

Call one fixed, reviewed boolean IPC query for an explicitly selected supported provider.

## Inputs

- **Provider:** choose from the menu. Only providers with an audited fixed endpoint can be queried; others remain metadata-only.

## Use

Select a provider and press **Capture**. Review the displayed endpoint/query. The result reports only the returned boolean or an unavailable/failed status.

## Limits

No polling, arbitrary endpoint, provider control, or configuration write. Metadata version differences are advisory. A true value does not certify installed implementation semantics.

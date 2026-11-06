# Housing Service Summary

**Tool ID:** `network.housing` · **Section:** Network

## Purpose

Fetch a public PaissaDB summary of reported open-plot count and observation age for one world/district.

## Inputs

- **Public world ID.**
- **District:** Mist, Lavender Beds, Goblet, Shirogane, or Empyreum.
- **Allow external request for this capture:** required consent.

## Use

Review the selected world/district and privacy notice, grant consent, then press **Capture**. One fixed HTTPS GET is sent; service sees the network IP.

## Limits

Reported open plots/age do not prove current availability or eligibility. No plot IDs, tenants, names, login, uploads, redirects, or automatic retries. Request is rate-limited and bounded.

# Copilot admin setup runbook

1. Create GitHub Enterprise/org configuration in GitHub using official docs.
2. Create a GitHub App or OAuth App only if the selected Copilot SDK/API flow requires it.
3. Store private key/client secret material in the operator vault/KMS and enter only references in RadioPad, for example `vault:copilot/oauth-client-secret`.
4. In RadioPad Admin → GitHub Copilot:
   - keep `Emergency disable` on until validation is complete;
   - select allowed modes, including `LocalCli` when using the official GitHub CLI runtime;
   - record org/enterprise slugs and app/client ids;
   - save secret references;
   - configure request/concurrency quotas;
   - run diagnostics.
5. For LocalCli runtime:
   - install the official GitHub CLI and Copilot extension on the desktop host;
   - sign in through the user page's token-free CLI bridge, or with `gh auth login --web`;
   - verify the user page shows CLI available, signed in, and entitlement allowed;
   - keep PHI/report context out of Copilot prompts because RadioPad blocks clinical context before spawn.
6. Do not enable SDK/enterprise-managed runtime until an official backend-safe SDK transport and token vault have been reviewed.

Current state: `LocalCli` is implemented for non-PHI coding assistance through Copilot CLI's prompt option stream over stdin. SDK/OAuth enterprise-managed and BYO modes remain policy/configuration surfaces until a backend-safe SDK transport is added.

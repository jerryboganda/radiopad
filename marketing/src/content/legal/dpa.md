---
title: "Data processing addendum"
description: "How RadioPad processes personal data on behalf of the institutions that use it."
updated: 2026-11-21
---

## Purpose of this addendum

This page summarises how [RadioPad legal entity] ("RadioPad") processes personal data on behalf of an institution that uses RadioPad (the "customer"). The customer decides why and how report data is processed. RadioPad processes it only to provide the service. The binding version of this addendum will form part of each customer agreement.

## Scope of processing

| Item | Description |
| --- | --- |
| Subject matter | Drafting, validating, storing and exporting radiology reports |
| People concerned | Patients whose studies are reported; the customer's radiologists, administrators and other users |
| Categories of data | Report content, including health information and identifiers such as accession numbers; user account data; audit records; operational data |
| Duration | For the term of the customer agreement, then as set out under Return and deletion |

## Customer instructions

RadioPad processes customer data only on the customer's documented instructions. The settings administrators choose in the product are part of those instructions: the AI provider, rulebooks, templates, prompt overrides and deployment model. If we believe an instruction conflicts with applicable law, we will tell the customer.

## AI providers

The customer chooses the AI provider for its tenant:

- **Anthropic Claude** or **Azure OpenAI**, as hosted providers. The report context needed for a draft is sent to that provider when a radiologist requests one.
- **A local model** through Ollama or vLLM. Drafting runs on the customer's own hardware, and no third-party AI provider receives report content.

Every AI call is recorded in the append-only audit log, including the user, model, prompt and rulebook versions, and whether the request contained patient data.

## Sub-processors

RadioPad uses sub-processors to host and operate the service. We will publish the list here and tell customers before adding or replacing one, so they can object.

[Sub-processor list to be published.]

## Security measures

- **Tenant isolation.** Each customer's reports, rulebooks and prompts stay inside its own tenant.
- **Append-only audit log.** Entries are written once and are not edited or deleted during the retention period.
- **Access control.** Single sign-on through OIDC, multi-factor authentication, and roles managed by the customer's administrators.
- **Human signature.** Reports are signed by a radiologist. The AI never signs, and AI-drafted text stays marked until reviewed.
- **Deployment choice.** On-prem or private cloud deployment for customers that need data to stay in their environment.
- **Signed connectors.** Plugins and connectors in the marketplace are signed before they are offered.

People at RadioPad who can access customer data are bound by confidentiality obligations and have access only where their work requires it.

## Security incidents

If we become aware of a security incident affecting customer data, we will notify the customer without undue delay and share what we know as we learn it. To report a suspected vulnerability, write to {{securityEmail}}.

[Notification window to be confirmed by counsel.]

## Helping customers meet their obligations

We help customers respond to requests from the people whose data they hold. Reports export as FHIR R4 DiagnosticReport, PDF, DOCX or plain text, and the `radiopad` command-line tool can export audit records.

## Return and deletion

At the end of the agreement the customer can export its reports and audit records. We then delete customer data from the service, except where the customer agreement or the law requires us to keep it. The audit log is kept for the period agreed with the customer and then deleted. For on-prem deployments, data stays with the customer throughout.

[Deletion timelines to be confirmed in the customer agreement.]

## Audits and information

We will make available the information a customer reasonably needs to check that we meet this addendum.

[Audit rights to be drafted by counsel.]

## International transfers

Where report data is processed depends on the customer's choices: the hosting region, the AI provider and the deployment model. With an on-prem deployment and a local model, report data stays in the customer's environment.

[Transfer terms to be drafted by counsel.]

## Contact

[RadioPad legal entity]. Email {{email}}.

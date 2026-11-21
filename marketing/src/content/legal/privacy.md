---
title: "Privacy policy"
description: "What data RadioPad and this website handle, why, where it goes, and who decides."
updated: 2026-11-21
---

## Who this policy covers

This policy is provided by [RadioPad legal entity] ("RadioPad", "we", "us"). It covers this website and the RadioPad software: the Windows desktop app, the mobile companion for Android and iOS, the web admin console, the RadioPad API and the `radiopad` command-line tool.

## Two roles: your institution and RadioPad

When a hospital, imaging group or practice uses RadioPad, that institution decides which clinical data enters the product and why. RadioPad processes that data on the institution's instructions, under its agreement with us and the [data processing addendum](/legal/dpa/).

Patients are not RadioPad users. If you have a question about a report from your own imaging study, contact the institution that performed the study. It holds the report and can answer for how it is used.

## Data the product processes

- **Report content.** Dictation, structured findings, prior reports, measurements and study metadata that a radiologist works with, and the draft and signed reports produced from them. This can include health information and identifiers such as accession numbers.
- **Account data.** Name, work email, role and institution, and the identity-provider subject when you sign in through single sign-on (OIDC).
- **Configuration.** Rulebooks, templates, prompt overrides and AI provider settings that administrators set for their institution.
- **Audit records.** An append-only log of AI calls and report actions, including the user, the institution, the model, the prompt and rulebook versions, and whether the request contained patient data.
- **Operational data.** IP address, device and app version, timestamps and error details needed to run and secure the service.

## How AI drafting uses report content

Each institution chooses the AI provider for its own tenant: Anthropic Claude, Azure OpenAI, or a local model run through Ollama or vLLM.

When a radiologist asks for a draft, the report context needed for that draft is sent to the chosen provider, and the provider returns draft text. With a local model on the institution's own hardware, that content does not leave the institution's network.

Hosted providers process requests under their own terms with the institution. Whether a provider may retain request data is set by those terms, and the institution is responsible for having the agreements it needs in place. Every AI call is written to the audit log, whichever provider handles it.

AI-drafted text stays marked in the report until a radiologist reviews or edits it. RadioPad never signs a report. See [Responsible AI](/legal/responsible-ai/).

## Where data is stored

- **Hosted service.** Data is held in the RadioPad service and separated by institution (tenant isolation). One institution's reports, rulebooks and prompts are not visible to another.
- **On-prem or private cloud.** The institution runs RadioPad in its own environment, and data stays there. We have access only if the institution grants it, for example for a support session.
- **Workstations and phones.** The desktop app works with report data on the radiologist's Windows workstation, and on-device dictation transcribes speech on that workstation. The mobile companion pairs to a live desktop session to carry dictation to it. Reports cannot be edited or signed on the phone.

## How long data is kept

Institutions set how long reports are kept in their agreement with us. The audit log is append-only: entries are not edited or deleted during the agreed retention period. Operational logs are kept for a limited period and then removed.

[Retention periods to be confirmed in the customer agreement.]

## Who we share data with

We do not sell personal data, and we do not use report content for advertising. We share data only:

- with the AI provider the institution has chosen, to produce drafts;
- with infrastructure providers that host the service, listed in the [data processing addendum](/legal/dpa/);
- with the institution's own systems it connects, such as PACS, RIS or FHIR endpoints;
- when the law requires it.

## This website

This website does not run analytics or advertising scripts. Our hosting and network providers process standard request data, such as IP address, browser type and time of visit, to deliver and protect the site, and may set strictly necessary security cookies.

If you book a demo or write to us, we receive the details you enter, such as your name, work email, institution and message, and use them to reply.

## Security

RadioPad enforces tenant isolation, records every AI call in an append-only audit log, and supports single sign-on through OIDC with multi-factor authentication. Institutions that need it can deploy on-prem or in a private cloud, with a local model. To report a vulnerability, write to {{securityEmail}}.

## Your choices

If your information appears in a report, contact the institution that holds it. For your own RadioPad account or a website enquiry, write to {{email}} and we will help you access, correct or delete your information where we can.

## Changes to this policy

When this policy changes, we update the date at the top of this page. If a change affects how customer data is processed, we tell customers before it takes effect.

## Contact

[RadioPad legal entity]. Email {{email}}.

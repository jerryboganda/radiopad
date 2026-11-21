---
title: "Responsible AI"
description: "How RadioPad keeps the radiologist in charge of every report: marked AI text, rulebook checks, drift monitoring and a human signature."
updated: 2026-11-21
---

## The radiologist is the final authority

RadioPad drafts, validates and standardises. The radiologist reads the study, reviews the draft, edits it and signs. Nothing in the product changes that order, and no setting lets the AI take the last step.

## AI text stays marked until reviewed

When RadioPad drafts a sentence, the sentence carries a visible marker: AI draft, requires review. The marker stays until a radiologist reviews or edits that text. At no point does machine-drafted text quietly become indistinguishable from the radiologist's own words.

## Rulebooks check every draft before signature

Each institution writes its own rulebooks, and every draft runs against them. Rules cover the problems that cause real corrections:

- laterality conflicts between findings and impression;
- required sections that are missing;
- critical results that need documented communication;
- measurements that disagree across the report;
- negation conflicts;
- follow-up recommendations that do not use approved phrasing.

Each rule has a severity. Blockers stop sign-off until they are resolved. Warnings and information stay visible to the radiologist. Rulebooks are authored and tested with the `radiopad` command-line tool before they go live.

## A human signature, every time

Only a radiologist can sign a report. There is no automatic sign-off. Integrations, batch generation and the command-line tool produce drafts, and signing is always an action a radiologist takes. The mobile companion carries dictation only: reports cannot be edited or signed on the phone.

## Every AI call is recorded

Every AI call is written to an append-only audit log with the user, the institution, the model, the prompt and rulebook versions, and whether the request contained patient data. Entries are written once and are not edited or deleted, so an institution can reconstruct how any draft was produced.

## Drift monitoring

Model behaviour can shift after a provider update, a new prompt or a change in case mix. RadioPad monitors AI output over time and raises alerts when it moves, so administrators can review the change before it becomes routine. Institutions can set their own prompt overrides, and the prompt version used is recorded with each call.

## Your choice of model

Institutions choose the provider that drafts their reports: Anthropic Claude, Azure OpenAI, or a local model through Ollama or vLLM. With an on-prem deployment and a local model, report content stays inside the institution's network.

## What RadioPad will not do

- Sign or send a report.
- Present AI-drafted text as reviewed when no radiologist has reviewed it.
- Read the images or make a diagnosis in place of the radiologist.
- Make an AI call that does not appear in the audit log.
- Edit or delete audit log entries during the retention period.
- Let one institution's reports, rulebooks or prompts reach another institution's tenant.

## Known limits

AI drafts can be wrong, incomplete or confidently phrased. Rulebook validation catches the rules an institution has written, and nothing beyond them. It does not assess the images. That is why the radiologist reads every study and every line of the report before signing.

## Raising a concern

If you see RadioPad behave in a way that contradicts this page, write to {{email}}. For security issues, write to {{securityEmail}}.

/**
 * Single source of truth for primary navigation IA.
 * Update i18n keys in messages/<locale>.json for labels.
 */

import type { ComponentType, SVGProps } from 'react';
import {
  FileText, ClipboardCheck, ScrollText, BarChart3, BookOpen, LayoutTemplate,
  MessageSquareText, Store, BookText, Server, Network, HardDrive,
  FileInput, WifiOff, Scale, FlaskConical, ShieldCheck, Flag, CreditCard,
  Activity, Settings2, Fingerprint, Users, ScanLine, Bone, Cpu,
  LayoutDashboard, ListTodo, Layers, Sparkles, Library, BadgeCheck,
  KeyRound, GraduationCap, UsersRound, AlertTriangle, Bell, FileStack, Smartphone,
} from 'lucide-react';
import type { PermissionKey } from '@/lib/permissions';
import { UBAG_HUB_ROLES } from '@/lib/roles';
import type { Surface } from '@/lib/surface';

export type NavIcon = ComponentType<SVGProps<SVGSVGElement>>;

export interface NavItem {
  href: string;
  /** i18n key under `nav.*` */
  labelKey: string;
  icon: NavIcon;
  /** Treat any path starting with this prefix as active. Defaults to exact + `/${href}/`. */
  matchPrefix?: string;
  /**
   * Permission key required to SEE this item. Omitted → always visible to any
   * signed-in user. Gating mirrors the backend RBAC (server still enforces).
   */
  permission?: PermissionKey;
  /**
   * Role ordinals (lib/roles `UserRole`) allowed to SEE this item. Used where
   * the backend gates by role rather than a permission key (e.g. UBAG Hub).
   * Omitted → no role restriction. The server still enforces.
   */
  roles?: readonly number[];
  /**
   * Surfaces this item belongs on (`desktop` = reporting product, `web` =
   * master admin, `mobile` = companion). Omitted → inherits the group's
   * `surfaces`; omitted on both → visible on every surface (shared).
   */
  surfaces?: readonly Surface[];
}

export interface NavGroup {
  /** i18n key under `nav.groups.*` */
  labelKey: string;
  /** Default surfaces for items in this group; each item may override. */
  surfaces?: readonly Surface[];
  /** Folded behind a single sidebar row (with `icon`) until opened or active. */
  collapsible?: boolean;
  icon?: NavIcon;
  items: NavItem[];
}

// RC icon set — lucide-react.
export const Icons: Record<string, NavIcon> = {
  dashboard: LayoutDashboard,
  worklist: ListTodo,
  criticalResults: AlertTriangle,
  protocols: Layers,
  aiAssistant: Sparkles,
  findingsLibrary: Library,
  quality: BadgeCheck,
  peerReview: UsersRound,
  notifications: Bell,
  devicePairing: Smartphone,
  reports: FileText,
  validation: ClipboardCheck,
  audit: ScrollText,
  analytics: BarChart3,
  rulebooks: BookOpen,
  templates: LayoutTemplate,
  reportTemplates: FileStack,
  prompts: MessageSquareText,
  marketplace: Store,
  terminology: BookText,
  teaching: GraduationCap,
  modalities: ScanLine,
  bodyParts: Bone,
  providers: Server,
  ubag: Network,
  pacs: HardDrive,
  onDeviceModels: Cpu,
  fhir: FileInput,
  offline: WifiOff,
  governance: Scale,
  modelEval: FlaskConical,
  security: ShieldCheck,
  flags: Flag,
  billing: CreditCard,
  usage: Activity,
  settings: Settings2,
  signInDevices: Fingerprint,
  users: Users,
  sso: KeyRound,
};

export const navGroups: NavGroup[] = [
  {
    // The reading flow — always visible. Everything else folds into the three
    // collapsible groups below so the sidebar stays short (owner decision 2026-11-21).
    labelKey: 'workspace',
    surfaces: ['desktop'],
    items: [
      { href: '/dashboard', labelKey: 'dashboard', icon: Icons.dashboard, permission: 'reports.read' },
      { href: '/worklist', labelKey: 'worklist', icon: Icons.worklist, permission: 'reports.read' },
      // PRD §14.15 — open critical-communication loops are an actionable queue, so
      // they stay top-level rather than hiding in a folded group.
      { href: '/critical-results', labelKey: 'criticalResults', icon: Icons.criticalResults, permission: 'critical_results.read' },
      { href: '/reports', labelKey: 'reports', icon: Icons.reports, permission: 'reports.read' },
      { href: '/ai-assistant', labelKey: 'aiAssistant', icon: Icons.aiAssistant },
    ],
  },
  {
    // Clinical content authoring — every template/rule/catalog page in one place.
    labelKey: 'library',
    surfaces: ['desktop'],
    collapsible: true,
    icon: Icons.findingsLibrary,
    items: [
      { href: '/templates', labelKey: 'templates', icon: Icons.templates, permission: 'templates.read' },
      // REPORT-TEMPLATES — per-radiologist output-document designer. Distinct from
      // /templates above (clinical section-content scaffolding, not layout).
      { href: '/report-templates', labelKey: 'reportTemplates', icon: Icons.reportTemplates, permission: 'templates.read' },
      { href: '/findings-library', labelKey: 'findingsLibrary', icon: Icons.findingsLibrary, permission: 'templates.read' },
      { href: '/protocols', labelKey: 'protocols', icon: Icons.protocols },
      { href: '/rulebooks', labelKey: 'rulebooks', icon: Icons.rulebooks, permission: 'rulebooks.read' },
      { href: '/prompts', labelKey: 'prompts', icon: Icons.prompts, permission: 'prompt_overrides.manage' },
      { href: '/modalities', labelKey: 'modalities', icon: Icons.modalities, permission: 'modalities.read' },
      { href: '/body-parts', labelKey: 'bodyParts', icon: Icons.bodyParts, permission: 'body_parts.read' },
      { href: '/terminology', labelKey: 'terminology', icon: Icons.terminology },
      // PRD §14.14 (TF-001..008) — the de-identified teaching library.
      { href: '/teaching', labelKey: 'teaching', icon: Icons.teaching, permission: 'teaching_cases.read', matchPrefix: '/teaching' },
      { href: '/marketplace', labelKey: 'marketplace', icon: Icons.marketplace },
    ],
  },
  {
    // Records & insight.
    labelKey: 'quality',
    surfaces: ['desktop'],
    collapsible: true,
    icon: Icons.quality,
    items: [
      { href: '/peer-review', labelKey: 'peerReview', icon: Icons.peerReview, permission: 'peer_review.read' },
      { href: '/quality', labelKey: 'quality', icon: Icons.quality, permission: 'reports.read' },
      { href: '/analytics', labelKey: 'analytics', icon: Icons.analytics, permission: 'reports.read' },
      { href: '/validation', labelKey: 'validation', icon: Icons.validation, permission: 'validation_packs.read' },
      { href: '/audit', labelKey: 'audit', icon: Icons.audit, permission: 'audit.read' },
    ],
  },
  {
    // Provider / PACS wiring is platform administration → web.
    labelKey: 'integrations',
    surfaces: ['web'],
    items: [
      { href: '/providers', labelKey: 'providers', icon: Icons.providers, permission: 'providers.read' },
      // Backend /api/ubag gates by ROLE (ItAdmin / ReportingAdmin / MedicalDirector
      // / ComplianceReviewer), not a permission key — mirror that here.
      { href: '/admin/ubag', labelKey: 'ubag', icon: Icons.ubag, roles: UBAG_HUB_ROLES },
      { href: '/admin/pacs', labelKey: 'pacs', icon: Icons.pacs, permission: 'tenant_settings.manage' },
    ],
  },
  {
    // Master-admin / platform operations → web only ("Users & Teams" +
    // "System Settings" of the RC mockups live here on the admin surface).
    labelKey: 'admin',
    surfaces: ['web'],
    items: [
      { href: '/admin/users', labelKey: 'users', icon: Icons.users, permission: 'users.manage' },
      { href: '/admin/governance', labelKey: 'governance', icon: Icons.governance, permission: 'audit.verify' },
      { href: '/admin/model-eval', labelKey: 'modelEval', icon: Icons.modelEval, permission: 'audit.verify' },
      { href: '/admin/validation-packs', labelKey: 'validationPacks', icon: Icons.validation, permission: 'validation_packs.read' },
      { href: '/admin/security', labelKey: 'security', icon: Icons.security, permission: 'security.manage' },
      { href: '/admin/sso', labelKey: 'sso', icon: Icons.sso, permission: 'security.manage' },
      { href: '/admin/feature-flags', labelKey: 'featureFlags', icon: Icons.flags, permission: 'billing.read' },
      { href: '/admin/billing', labelKey: 'billing', icon: Icons.billing, permission: 'billing.read' },
      { href: '/admin/usage', labelKey: 'usage', icon: Icons.usage, permission: 'audit.read' },
      { href: '/admin/settings', labelKey: 'settings', icon: Icons.settings, permission: 'tenant_settings.manage' },
    ],
  },
  {
    // Desktop personal setup: one group for everything a reader configures.
    labelKey: 'settings',
    surfaces: ['desktop'],
    collapsible: true,
    icon: Icons.settings,
    items: [
      { href: '/settings', labelKey: 'settingsHub', icon: Icons.settings },
      // The on-device engines run on the desktop, so their manager ships here.
      { href: '/settings/models', labelKey: 'onDeviceModels', icon: Icons.onDeviceModels },
      // Device pairing hub — pair mobile companion mic & remote before reporting.
      { href: '/device-pairing', labelKey: 'devicePairing', icon: Icons.devicePairing },
      // NOTIF-001 — the personal notifications inbox (also reachable from the topbar bell).
      { href: '/notifications', labelKey: 'notifications', icon: Icons.notifications },
      { href: '/account/security', labelKey: 'signInDevices', icon: Icons.signInDevices },
      // FHIR import seeds a draft report → part of the reporting product.
      { href: '/admin/fhir-import', labelKey: 'fhirImport', icon: Icons.fhir, permission: 'reports.draft' },
      { href: '/offline', labelKey: 'offline', icon: Icons.offline },
    ],
  },
  {
    // Shared sign-in devices (web + mobile; desktop lists it under Settings) and the
    // mobile companion surface navigation.
    labelKey: 'account',
    items: [
      { href: '/account/security', labelKey: 'signInDevices', icon: Icons.signInDevices, surfaces: ['web', 'mobile'] },
      { href: '/companion', labelKey: 'companion', icon: Icons.aiAssistant, surfaces: ['mobile'] },
      // Standalone mobile reporting (create report → multi-take dictation → hand off to
      // desktop Positive Findings for transcription). Requires a full signed-in session.
      { href: '/reporting', labelKey: 'reporting', icon: Icons.reports, surfaces: ['mobile'] },
    ],
  },
];

export function isActive(pathname: string, item: NavItem): boolean {
  if (item.matchPrefix && pathname.startsWith(item.matchPrefix)) return true;
  if (item.href === '/') return pathname === '/';
  return pathname === item.href || pathname.startsWith(item.href + '/');
}

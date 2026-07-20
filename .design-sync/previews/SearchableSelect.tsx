import { useEffect, useRef, useState, type ReactNode } from 'react';
import { SearchableSelect } from '@radiopad/frontend';

// The report editor's Rulebook picker fixture — long enough to warrant search.
const RULEBOOKS = [
  { value: 'chest_ct_v1', label: 'Chest CT (v1)', searchText: 'thorax lung nodule' },
  { value: 'chest_xray_v2', label: 'Chest X-ray (v2)', searchText: 'cxr thorax' },
  { value: 'ct_pulmonary_angio_v1', label: 'CT Pulmonary Angiogram (v1)', searchText: 'pe embolism' },
  { value: 'abdomen_pelvis_ct_v1', label: 'Abdomen–Pelvis CT (v1)', searchText: 'liver kidney bowel' },
  { value: 'brain_mri_v3', label: 'Brain MRI (v3)', searchText: 'neuro head stroke' },
  { value: 'msk_knee_mri_v1', label: 'MSK Knee MRI (v1)', searchText: 'meniscus acl cartilage' },
  { value: 'thyroid_us_v2', label: 'Thyroid Ultrasound (v2)', searchText: 'tirads neck nodule' },
  { value: 'pet_ct_oncology_draft', label: 'PET-CT Oncology (draft)', disabled: true },
];

function Field({ label, htmlFor, children }: { label: string; htmlFor: string; children: ReactNode }) {
  return (
    <div style={{ display: 'grid', gap: 6, maxWidth: 340 }}>
      <label htmlFor={htmlFor} style={{ fontSize: 12, fontWeight: 600, color: 'var(--text-muted)' }}>
        {label}
      </label>
      {children}
    </div>
  );
}

// Closed trigger with a bound rulebook — the everyday resting state.
export const SelectedClosed = () => (
  <Field label="Rulebook" htmlFor="rb-selected">
    <SearchableSelect id="rb-selected" options={RULEBOOKS} value="chest_ct_v1" onChange={() => {}} />
  </Field>
);

// Nothing bound yet — placeholder text plus the clearable "— none —" row.
export const PlaceholderWithNone = () => (
  <Field label="Rulebook" htmlFor="rb-placeholder">
    <SearchableSelect
      id="rb-placeholder"
      options={RULEBOOKS}
      value={null}
      onChange={() => {}}
      placeholder="Select a rulebook…"
      searchPlaceholder="Search rulebooks…"
      includeNone
      noneLabel="— no rulebook —"
    />
  </Field>
);

// Popover open: search box focused, list showing the bound option highlighted
// and a disabled draft row. Opened by clicking the trigger on mount (there is
// no `open` prop — internal state only).
export const OpenPopover = () => {
  const [value, setValue] = useState<string | null>('chest_ct_v1');
  const rootRef = useRef<HTMLDivElement | null>(null);
  useEffect(() => {
    rootRef.current?.querySelector<HTMLButtonElement>('.rp-combobox-trigger')?.click();
  }, []);
  return (
    <div ref={rootRef} style={{ minHeight: 420 }}>
      <Field label="Rulebook" htmlFor="rb-open">
        <SearchableSelect
          id="rb-open"
          options={RULEBOOKS}
          value={value}
          onChange={setValue}
          searchPlaceholder="Search rulebooks…"
        />
      </Field>
    </div>
  );
};

// Locked by the bound template — disabled trigger keeps the selected label.
export const DisabledLocked = () => (
  <Field label="Rulebook (set by template)" htmlFor="rb-locked">
    <SearchableSelect id="rb-locked" options={RULEBOOKS} value="brain_mri_v3" onChange={() => {}} disabled />
  </Field>
);

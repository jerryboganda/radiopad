// SearchableSelect — a combobox that replaces a native <select> where the
// option list is long enough to want a type-to-filter search box. These tests
// pin the behaviour the report-editor dropdowns rely on: filtering, keyboard
// selection, a clearable "— none —" row, disabled options, and Escape-to-close.
import { describe, it, expect, vi } from 'vitest';
import { render, fireEvent, within } from '@testing-library/react';
import * as React from 'react';
import SearchableSelect, { type SearchableSelectOption } from '@/components/ui/SearchableSelect';

const OPTIONS: SearchableSelectOption[] = [
  { value: 'a', label: 'Apple', searchText: 'fruit red' },
  { value: 'b', label: 'Banana' },
  { value: 'c', label: 'Cherry', disabled: true },
];

function open(getByRole: ReturnType<typeof render>['getByRole']) {
  fireEvent.click(getByRole('combobox'));
}

describe('SearchableSelect', () => {
  it('shows the placeholder when nothing is selected and the label when a value is set', () => {
    const { getByRole, rerender } = render(
      <SearchableSelect options={OPTIONS} value={null} onChange={() => {}} placeholder="Pick one" />,
    );
    expect(getByRole('combobox').textContent).toContain('Pick one');

    rerender(<SearchableSelect options={OPTIONS} value="b" onChange={() => {}} placeholder="Pick one" />);
    expect(getByRole('combobox').textContent).toContain('Banana');
  });

  it('opens on trigger click, shows a search box, and lists every option', () => {
    const { getByRole, getByPlaceholderText, getAllByRole } = render(
      <SearchableSelect options={OPTIONS} value={null} onChange={() => {}} searchPlaceholder="Search…" />,
    );
    open(getByRole);
    expect(getByPlaceholderText('Search…')).toBeInTheDocument();
    expect(getAllByRole('option')).toHaveLength(3);
  });

  it('filters case-insensitively over label and searchText, and shows an empty state', () => {
    const { getByRole, getByPlaceholderText, getAllByRole, queryAllByRole, getByText } = render(
      <SearchableSelect options={OPTIONS} value={null} onChange={() => {}} searchPlaceholder="Search…" emptyLabel="No matches" />,
    );
    open(getByRole);
    const search = getByPlaceholderText('Search…');

    fireEvent.change(search, { target: { value: 'APP' } });
    expect(getAllByRole('option')).toHaveLength(1);
    expect(getAllByRole('option')[0].textContent).toContain('Apple');

    // matches via searchText ("fruit red"), not the visible label
    fireEvent.change(search, { target: { value: 'red' } });
    expect(getAllByRole('option')).toHaveLength(1);
    expect(getAllByRole('option')[0].textContent).toContain('Apple');

    fireEvent.change(search, { target: { value: 'zzz' } });
    expect(queryAllByRole('option')).toHaveLength(0);
    expect(getByText('No matches')).toBeInTheDocument();
  });

  it('selects the highlighted option with ArrowDown + Enter', () => {
    const onChange = vi.fn();
    const { getByRole, getByPlaceholderText } = render(
      <SearchableSelect options={OPTIONS} value={null} onChange={onChange} searchPlaceholder="Search…" />,
    );
    open(getByRole);
    const search = getByPlaceholderText('Search…');
    fireEvent.keyDown(search, { key: 'ArrowDown' }); // highlight first (Apple)
    fireEvent.keyDown(search, { key: 'Enter' });
    expect(onChange).toHaveBeenCalledWith('a');
  });

  it('renders a clearable none row that emits null when includeNone is set', () => {
    const onChange = vi.fn();
    const { getByRole, getByText } = render(
      <SearchableSelect options={OPTIONS} value="b" onChange={onChange} includeNone noneLabel="— none —" />,
    );
    open(getByRole);
    fireEvent.mouseDown(getByText('— none —'));
    expect(onChange).toHaveBeenCalledWith(null);
  });

  it('does not select a disabled option', () => {
    const onChange = vi.fn();
    const { getByRole, getByText } = render(
      <SearchableSelect options={OPTIONS} value={null} onChange={onChange} />,
    );
    open(getByRole);
    fireEvent.mouseDown(getByText('Cherry')); // disabled
    expect(onChange).not.toHaveBeenCalled();
  });

  it('selects an enabled option on mouse down', () => {
    const onChange = vi.fn();
    const { getByRole, getByText } = render(
      <SearchableSelect options={OPTIONS} value={null} onChange={onChange} />,
    );
    open(getByRole);
    fireEvent.mouseDown(getByText('Banana'));
    expect(onChange).toHaveBeenCalledWith('b');
  });

  it('closes on Escape without selecting', () => {
    const onChange = vi.fn();
    const { getByRole, getByPlaceholderText, queryByPlaceholderText } = render(
      <SearchableSelect options={OPTIONS} value={null} onChange={onChange} searchPlaceholder="Search…" />,
    );
    open(getByRole);
    const search = getByPlaceholderText('Search…');
    fireEvent.keyDown(search, { key: 'Escape' });
    expect(queryByPlaceholderText('Search…')).toBeNull();
    expect(onChange).not.toHaveBeenCalled();
  });
});

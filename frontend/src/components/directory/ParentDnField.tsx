import { useId } from "react";
import type { ChangeEvent, JSX, ReactNode } from "react";
import { FormHint } from "../primitives";

/**
 * The "parent DN" field, with suggestions. Existing containers are offered as a list, but the
 * field stays free text: the directory holds containers this screen does not enumerate
 * (CN=Users, CN=Computers, the base DN itself), and creating entries in them is legitimate.
 */
export interface ParentDnFieldProps {
  label: string;
  value: string;
  onChange: (value: string) => void;
  options: readonly string[];
  placeholder?: string | undefined;
  hint?: ReactNode;
}

export function ParentDnField({
  label,
  value,
  onChange,
  options,
  placeholder,
  hint,
}: ParentDnFieldProps): JSX.Element {
  const listId = useId();

  return (
    <div className="form-row">
      <label className="form-label">{label}</label>
      <input
        type="text"
        list={listId}
        value={value}
        placeholder={placeholder}
        onChange={(event: ChangeEvent<HTMLInputElement>) => onChange(event.target.value)}
      />
      <datalist id={listId}>
        {options.map((dn) => (
          <option key={dn} value={dn} />
        ))}
      </datalist>
      {hint !== undefined && hint !== null && hint !== false && <FormHint>{hint}</FormHint>}
    </div>
  );
}

/** The least a directory item must offer to serve as a parent-DN suggestion. */
interface DirectoryEntryLike {
  readonly distinguishedName: string;
  readonly parentDistinguishedName: string;
}

export interface ParentDnOptionsConfig {
  parents?: boolean | undefined;
  ownDn?: boolean | undefined;
}

/**
 * Suggestions are assembled from what is already on screen. What counts as a parent depends on
 * the entity: a unit nests inside another unit (ownDn), computers sit in containers that are not
 * in the list (parents), and groups take either.
 */
export function parentDnOptions(
  items: readonly DirectoryEntryLike[],
  { parents = true, ownDn = false }: ParentDnOptionsConfig = {}
): string[] {
  const dns: string[] = [];
  if (parents) dns.push(...items.map((item) => item.parentDistinguishedName));
  if (ownDn) dns.push(...items.map((item) => item.distinguishedName));
  return dns
    .filter((dn, index, all) => dn !== "" && all.indexOf(dn) === index)
    .sort((a, b) => a.localeCompare(b));
}

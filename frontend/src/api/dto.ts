/**
 * The shapes of Nelsar.Axia.Api's responses and request bodies.
 *
 * These are a translation of the C# contracts in `Nelsar.Axia.Contracts` (Common/Requests/
 * Responses), not guesses from the look of the JSON: ASP.NET serialises to camelCase by default,
 * a `Guid` arrives as a string and a `DateTimeOffset` as an ISO string. Optionality here means
 * exactly C#'s `?`: `string Description { get; set; } = ""` is always a string (possibly empty),
 * whereas `string? Details` is something that may be absent.
 */

/** A Guid from the backend. Named separately so a directory `id` cannot be mistaken for any old string. */
export type Uuid = string;

/** ISO-8601, from a DateTimeOffset. */
export type IsoDateTime = string;

/**
 * UserRoles.* from Nelsar.Axia.Application/Configuration/JwtSettings.cs. In C# it is a string,
 * but the set of values is closed, and a union pays for itself here: a typo in a role comparison
 * stops compiling.
 */
export type UserRole = "Admin" | "UserCreator" | "UserEditor" | "UserDeleter" | "User";

export const USER_ROLES: readonly UserRole[] = [
  "Admin",
  "UserCreator",
  "UserEditor",
  "UserDeleter",
  "User",
];

/** PagedResponse<T>. */
export interface PagedResponse<T> {
  items: T[];
  pageSize: number;
  pageToken: string | null;
  hasMore: boolean;
  /** false means the directory walk stopped early and the list is incomplete. */
  isComplete: boolean;
}

/** UserDto. Email/Username/Dn are strings that can be empty but are never absent. */
export interface UserDto {
  id: Uuid;
  email: string;
  username: string;
  name: string;
  /** Arrives from the backend as a string; an unknown value needs no widening of the union — see isUserRole. */
  role: UserRole;
  dn: string;
  groups: string[];
}

/** UserListingResponse (GET /api/v1/users). */
export interface UserListingResponse {
  items: UserDto[];
  isComplete: boolean;
}

/** DirectoryAttributeDto. */
export interface DirectoryAttributeDto {
  name: string;
  values: string[];
  isBinary: boolean;
  isRedacted: boolean;
}

/** UserAttributesDto. */
export interface UserAttributesDto {
  id: Uuid;
  dn: string;
  attributes: DirectoryAttributeDto[];
}

/** UserCreateRequest. */
export interface UserCreateRequest {
  email: string;
  name: string;
  password: string;
}

/** UserUpdateRequest: an empty password is not sent at all — that is what "leave it alone" means. */
export interface UserUpdateRequest {
  name: string;
  email?: string | undefined;
  password?: string | undefined;
}

/** BulkOperationStatus.*. */
export type BulkItemStatus = "completed" | "failed";

/** BulkJobStateValue.*. */
export type BulkJobState = "pending" | "inprogress" | "completed" | "failed";

/** BulkUserRequest. */
export interface BulkUserRequest {
  operation: string;
  email: string;
  name: string;
  password?: string | null;
  newEmail?: string | null;
}

/** BulkUserResult. */
export interface BulkUserResult {
  email: string;
  status: BulkItemStatus;
  message: string;
  user?: UserDto | null;
}

/** BulkOperationResponse<BulkUserResult>. */
export interface BulkOperationResponse {
  jobId: string;
  state: BulkJobState;
  items: BulkUserResult[];
  completedCount: number;
  failedCount: number;
  errorMessage?: string | null;
}

/** The response to POST /api/v1/users/bulk — 202 Accepted with a single field. */
export interface BulkEnqueueResponse {
  jobId: string;
}

/** PatternGeneratorRequest. */
export interface PatternGeneratorRequest {
  pattern: string;
  maxItems: number;
  rawText?: string | undefined;
  regexPattern?: string | undefined;
  usernameTemplate?: string | undefined;
  transliterate: boolean;
  checkConflicts: boolean;
}

/** PatternGeneratorItem. */
export interface PatternGeneratorItem {
  value: string;
  exists: boolean;
}

/** PatternGeneratorResponse: details only arrives when checkConflicts was asked for. */
export interface PatternGeneratorResponse {
  items: string[];
  count: number;
  details?: PatternGeneratorItem[] | null;
}

/** GroupDto. */
export interface GroupDto {
  id: Uuid;
  name: string;
  distinguishedName: string;
  parentDistinguishedName: string;
  description: string;
  members: string[];
}

/** CreateGroupRequest / UpdateGroupRequest (the membership is replaced wholesale). */
export interface GroupWriteRequest {
  name: string;
  parentDistinguishedName: string;
  description: string;
  memberDistinguishedNames: string[];
}

/** OrganizationalUnitDto. */
export interface OrganizationalUnitDto {
  id: Uuid;
  name: string;
  distinguishedName: string;
  parentDistinguishedName: string;
  description: string;
}

/** CreateOrganizationalUnitRequest / UpdateOrganizationalUnitRequest. */
export interface OrgUnitWriteRequest {
  name: string;
  parentDistinguishedName: string;
  description: string;
}

/** ComputerDto: enabled may be null — "the directory did not say", which is not the same as false. */
export interface ComputerDto {
  id: Uuid;
  name: string;
  distinguishedName: string;
  parentDistinguishedName: string;
  description: string;
  dnsHostName: string;
  operatingSystem: string;
  operatingSystemVersion: string;
  enabled: boolean | null;
}

/** CreateComputerRequest. */
export interface ComputerCreateRequest {
  name: string;
  parentDistinguishedName: string;
  description: string;
  dnsHostName: string;
}

/** UpdateComputerRequest. */
export interface ComputerUpdateRequest extends ComputerCreateRequest {
  enabled: boolean;
}

/** Which kind of directory provider this is. */
export type ProviderType = "StandardLdap" | "ActiveDirectory";

/**
 * SelectableProviderDto — a directory available to sign in to, as the anonymous
 * /api/v1/providers/selectable returns it: an id and a name, nothing about how it is reached.
 */
export interface SelectableProviderDto {
  id: Uuid;
  name: string;
}

/** ProviderDto. */
export interface ProviderDto {
  id: Uuid;
  name: string;
  host: string;
  port: number;
  providerType: ProviderType;
  isActive: boolean;
  failoverHosts: string | null;
  baseDn: string;
  adminUsername: string;
  userSearchFilter: string;
  userSearchPageSize: number;
}

/** ProviderCreateRequest / ProviderUpdateRequest. */
export interface ProviderWriteRequest {
  name: string;
  host: string;
  port: number;
  providerType: ProviderType;
  isActive: boolean;
  failoverHosts: string | null;
  baseDn: string;
  adminUsername: string;
  /** An empty password on update means "keep the current one", so the field is omitted. */
  adminPassword?: string | undefined;
  userSearchFilter: string;
  userSearchPageSize: number;
}

/** RoleMappingDto: providerId === null means "global, for every provider". */
export interface RoleMappingDto {
  id: Uuid;
  providerId: Uuid | null;
  groupName: string;
  roleName: UserRole;
  priority: number;
  /**
   * The mapping names its group by CN alone, from before mappings took a DN, and so matches every
   * group with that name anywhere in the directory. It still works; saving it requires a DN.
   */
  identifiesGroupByNameOnly: boolean;
}

/** RoleMappingCreateRequest / RoleMappingUpdateRequest. */
export interface RoleMappingWriteRequest {
  providerId: Uuid | null;
  groupName: string;
  roleName: UserRole;
  priority: number;
}

/** RateLimitSettingsDto. */
export interface RateLimitSettingsDto {
  id: Uuid;
  policyName: string;
  permitLimit: number;
  windowSeconds: number;
  queueLimit: number;
  enabled: boolean;
}

/** RateLimitSettingsUpdateRequest. */
export interface RateLimitUpsertRequest {
  permitLimit: number;
  windowSeconds: number;
  queueLimit: number;
  enabled: boolean;
}

/** AuditEventDto. */
export interface AuditEventDto {
  id: Uuid;
  eventType: string;
  objectId: Uuid | null;
  objectDn: string | null;
  actor: string;
  action: string;
  success: boolean;
  details: string | null;
  timestamp: IsoDateTime;
}

/** AuthenticateRequest. */
export interface AuthenticateRequest {
  email: string;
  password: string;
}

/** AuthenticateResponse. */
export interface AuthenticateResponse {
  token: string;
  user: UserDto;
}

/**
 * Narrows a string to a UserRole. The role arrives from the JWT and from API responses: if the
 * backend ever introduces a new one, an older frontend must not quietly treat it as familiar.
 */
export function isUserRole(value: unknown): value is UserRole {
  return typeof value === "string" && (USER_ROLES as readonly string[]).includes(value);
}

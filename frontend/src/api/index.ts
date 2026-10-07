import { qs, request, requestNoContent } from "./client";
import type {
  AuditEventDto,
  AuthenticateResponse,
  BulkEnqueueResponse,
  BulkOperationResponse,
  BulkUserRequest,
  ComputerCreateRequest,
  ComputerDto,
  ComputerUpdateRequest,
  GroupDto,
  GroupWriteRequest,
  OrgUnitWriteRequest,
  OrganizationalUnitDto,
  PagedResponse,
  PatternGeneratorRequest,
  PatternGeneratorResponse,
  ProviderDto,
  ProviderWriteRequest,
  RateLimitSettingsDto,
  RateLimitUpsertRequest,
  RoleMappingDto,
  RoleMappingWriteRequest,
  RolePermissionsDto,
  RolesOverviewDto,
  SelectableProviderDto,
  UserAttributesDto,
  UserCreateRequest,
  UserDto,
  UserListingResponse,
  UserUpdateRequest,
  Uuid,
} from "./dto";

export { ApiError } from "./client";

const DEFAULT_PAGE_SIZE = 50;

/** Every directory listing takes the same page parameters. */
interface PageQuery {
  filter?: string | undefined;
  pageSize?: number | undefined;
  pageToken?: string | undefined;
}

function pageQuery({ filter, pageSize = DEFAULT_PAGE_SIZE, pageToken }: PageQuery): string {
  return qs({ filter, pageSize, pageToken });
}

export const api = {
  auth: {
    /**
     * providerId is the directory the credentials are checked against. An empty string means the
     * default directory. This is the only moment a directory is chosen: from here on it is
     * recorded in the issued token, and requests no longer name it.
     */
    login: (email: string, password: string, providerId = ""): Promise<AuthenticateResponse> =>
      request<AuthenticateResponse>("/api/v1/auth/login", {
        method: "POST",
        body: { email, password },
        auth: false,
        providerId,
      }),
    register: (email: string, name: string, password: string, providerId = ""): Promise<UserDto> =>
      request<UserDto>("/api/v1/auth/register", {
        method: "POST",
        body: { email, name, password } satisfies UserCreateRequest,
        auth: false,
        providerId,
      }),
  },

  users: {
    getAll: (): Promise<UserListingResponse> => request<UserListingResponse>("/api/v1/users"),
    getById: (id: Uuid): Promise<UserDto> => request<UserDto>(`/api/v1/users/${id}`),
    /**
     * Every attribute of the entry, under the directory's own names. Deliberately separate from
     * getById: it is another trip to the directory, worth making only once a user is opened.
     */
    getAttributes: (id: Uuid): Promise<UserAttributesDto> =>
      request<UserAttributesDto>(`/api/v1/users/${id}/attributes`),
    search: (query: PageQuery): Promise<PagedResponse<UserDto>> =>
      request<PagedResponse<UserDto>>(`/api/v1/users/search${pageQuery(query)}`),
    create: (payload: UserCreateRequest): Promise<UserDto> =>
      request<UserDto>("/api/v1/users", { method: "POST", body: payload }),
    update: (id: Uuid, payload: UserUpdateRequest): Promise<UserDto> =>
      request<UserDto>(`/api/v1/users/${id}`, { method: "PUT", body: payload }),
    remove: (id: Uuid): Promise<void> => requestNoContent(`/api/v1/users/${id}`, { method: "DELETE" }),
    bulkSync: (items: BulkUserRequest[]): Promise<BulkOperationResponse> =>
      request<BulkOperationResponse>("/api/v1/users/bulk/sync", { method: "POST", body: items }),
    bulkEnqueue: (items: BulkUserRequest[]): Promise<BulkEnqueueResponse> =>
      request<BulkEnqueueResponse>("/api/v1/users/bulk", { method: "POST", body: items }),
    /** The backend may answer with an empty body for a job that has not started — hence null. */
    bulkStatus: (jobId: string): Promise<BulkOperationResponse | null> =>
      request<BulkOperationResponse | null>(`/api/v1/users/bulk/status/${jobId}`),
    generate: (payload: PatternGeneratorRequest): Promise<PatternGeneratorResponse> =>
      request<PatternGeneratorResponse>("/api/v1/users/generate", { method: "POST", body: payload }),
  },

  /**
   * As with OUs, `id` is a hash of the entry's DN, so it changes when the entry is renamed or
   * moved: always work from identifiers taken from a fresh listing. A group's membership is a
   * list of DNs, and an update replaces it wholesale.
   */
  groups: {
    search: (query: PageQuery): Promise<PagedResponse<GroupDto>> =>
      request<PagedResponse<GroupDto>>(`/api/v1/groups${pageQuery(query)}`),
    getById: (id: Uuid): Promise<GroupDto> => request<GroupDto>(`/api/v1/groups/${id}`),
    create: (payload: GroupWriteRequest): Promise<GroupDto> =>
      request<GroupDto>("/api/v1/groups", { method: "POST", body: payload }),
    update: (id: Uuid, payload: GroupWriteRequest): Promise<GroupDto> =>
      request<GroupDto>(`/api/v1/groups/${id}`, { method: "PUT", body: payload }),
    remove: (id: Uuid): Promise<void> => requestNoContent(`/api/v1/groups/${id}`, { method: "DELETE" }),
  },

  orgUnits: {
    search: (query: PageQuery): Promise<PagedResponse<OrganizationalUnitDto>> =>
      request<PagedResponse<OrganizationalUnitDto>>(`/api/v1/organizational-units${pageQuery(query)}`),
    getById: (id: Uuid): Promise<OrganizationalUnitDto> =>
      request<OrganizationalUnitDto>(`/api/v1/organizational-units/${id}`),
    create: (payload: OrgUnitWriteRequest): Promise<OrganizationalUnitDto> =>
      request<OrganizationalUnitDto>("/api/v1/organizational-units", { method: "POST", body: payload }),
    update: (id: Uuid, payload: OrgUnitWriteRequest): Promise<OrganizationalUnitDto> =>
      request<OrganizationalUnitDto>(`/api/v1/organizational-units/${id}`, { method: "PUT", body: payload }),
    remove: (id: Uuid): Promise<void> =>
      requestNoContent(`/api/v1/organizational-units/${id}`, { method: "DELETE" }),
  },

  /**
   * Computer accounts, addressed by a hash of the DN as groups and OUs are. What an entry means
   * matters here: this manages directory entries rather than machines — deleting one breaks the
   * machine's domain trust and forces it to rejoin.
   */
  computers: {
    search: (query: PageQuery): Promise<PagedResponse<ComputerDto>> =>
      request<PagedResponse<ComputerDto>>(`/api/v1/computers${pageQuery(query)}`),
    getById: (id: Uuid): Promise<ComputerDto> => request<ComputerDto>(`/api/v1/computers/${id}`),
    create: (payload: ComputerCreateRequest): Promise<ComputerDto> =>
      request<ComputerDto>("/api/v1/computers", { method: "POST", body: payload }),
    update: (id: Uuid, payload: ComputerUpdateRequest): Promise<ComputerDto> =>
      request<ComputerDto>(`/api/v1/computers/${id}`, { method: "PUT", body: payload }),
    remove: (id: Uuid): Promise<void> => requestNoContent(`/api/v1/computers/${id}`, { method: "DELETE" }),
  },

  providers: {
    /**
     * The list of directories for the sign-in form: the id and name of active providers, with no
     * authorisation. Separate from getAll, which needs providers.manage and returns connection
     * addresses the sign-in screen neither needs nor should see.
     */
    getSelectable: (): Promise<SelectableProviderDto[]> =>
      request<SelectableProviderDto[]>("/api/v1/providers/selectable", { auth: false }),
    getAll: (): Promise<ProviderDto[]> => request<ProviderDto[]>("/api/v1/providers"),
    getById: (id: Uuid): Promise<ProviderDto> => request<ProviderDto>(`/api/v1/providers/${id}`),
    create: (payload: ProviderWriteRequest): Promise<ProviderDto> =>
      request<ProviderDto>("/api/v1/providers", { method: "POST", body: payload }),
    update: (id: Uuid, payload: ProviderWriteRequest): Promise<ProviderDto> =>
      request<ProviderDto>(`/api/v1/providers/${id}`, { method: "PUT", body: payload }),
    remove: (id: Uuid): Promise<void> =>
      requestNoContent(`/api/v1/providers/${id}`, { method: "DELETE" }),
  },

  me: {
    /** What the API will allow this session now. Not rate-limited like sign-in, so it can be asked often. */
    permissions: (): Promise<string[]> => request<string[]>("/api/v1/me/permissions"),
  },

  roles: {
    getAll: (): Promise<RolesOverviewDto> => request<RolesOverviewDto>("/api/v1/roles"),
    /** Replaces what the role holds: a permission left out is revoked. */
    updatePermissions: (roleName: string, permissions: readonly string[]): Promise<RolePermissionsDto> =>
      request<RolePermissionsDto>(`/api/v1/roles/${encodeURIComponent(roleName)}/permissions`, {
        method: "PUT",
        body: { permissions },
      }),
  },

  roleMappings: {
    getAll: (): Promise<RoleMappingDto[]> => request<RoleMappingDto[]>("/api/v1/role-mappings"),
    create: (payload: RoleMappingWriteRequest): Promise<RoleMappingDto> =>
      request<RoleMappingDto>("/api/v1/role-mappings", { method: "POST", body: payload }),
    update: (id: Uuid, payload: RoleMappingWriteRequest): Promise<RoleMappingDto> =>
      request<RoleMappingDto>(`/api/v1/role-mappings/${id}`, { method: "PUT", body: payload }),
    remove: (id: Uuid): Promise<void> =>
      requestNoContent(`/api/v1/role-mappings/${id}`, { method: "DELETE" }),
  },

  rateLimits: {
    getAll: (): Promise<RateLimitSettingsDto[]> =>
      request<RateLimitSettingsDto[]>("/api/v1/rate-limit-settings"),
    upsert: (policyName: string, payload: RateLimitUpsertRequest): Promise<RateLimitSettingsDto> =>
      request<RateLimitSettingsDto>(`/api/v1/rate-limit-settings/${encodeURIComponent(policyName)}`, {
        method: "PUT",
        body: payload,
      }),
  },

  audit: {
    getRecent: (pageSize = DEFAULT_PAGE_SIZE, pageToken?: string): Promise<PagedResponse<AuditEventDto>> =>
      request<PagedResponse<AuditEventDto>>(`/api/v1/audit-events${qs({ pageSize, pageToken })}`),
  },
} as const;

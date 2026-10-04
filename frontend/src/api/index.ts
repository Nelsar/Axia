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
      request<AuthenticateResponse>("/api/auth/login", {
        method: "POST",
        body: { email, password },
        auth: false,
        providerId,
      }),
    register: (email: string, name: string, password: string, providerId = ""): Promise<UserDto> =>
      request<UserDto>("/api/auth/register", {
        method: "POST",
        body: { email, name, password } satisfies UserCreateRequest,
        auth: false,
        providerId,
      }),
  },

  users: {
    getAll: (): Promise<UserListingResponse> => request<UserListingResponse>("/api/users"),
    getById: (id: Uuid): Promise<UserDto> => request<UserDto>(`/api/users/${id}`),
    /**
     * Every attribute of the entry, under the directory's own names. Deliberately separate from
     * getById: it is another trip to the directory, worth making only once a user is opened.
     */
    getAttributes: (id: Uuid): Promise<UserAttributesDto> =>
      request<UserAttributesDto>(`/api/users/${id}/attributes`),
    search: (query: PageQuery): Promise<PagedResponse<UserDto>> =>
      request<PagedResponse<UserDto>>(`/api/users/search${pageQuery(query)}`),
    create: (payload: UserCreateRequest): Promise<UserDto> =>
      request<UserDto>("/api/users", { method: "POST", body: payload }),
    update: (id: Uuid, payload: UserUpdateRequest): Promise<UserDto> =>
      request<UserDto>(`/api/users/${id}`, { method: "PUT", body: payload }),
    remove: (id: Uuid): Promise<void> => requestNoContent(`/api/users/${id}`, { method: "DELETE" }),
    bulkSync: (items: BulkUserRequest[]): Promise<BulkOperationResponse> =>
      request<BulkOperationResponse>("/api/users/bulk/sync", { method: "POST", body: items }),
    bulkEnqueue: (items: BulkUserRequest[]): Promise<BulkEnqueueResponse> =>
      request<BulkEnqueueResponse>("/api/users/bulk", { method: "POST", body: items }),
    /** The backend may answer with an empty body for a job that has not started — hence null. */
    bulkStatus: (jobId: string): Promise<BulkOperationResponse | null> =>
      request<BulkOperationResponse | null>(`/api/users/bulk/status/${jobId}`),
    generate: (payload: PatternGeneratorRequest): Promise<PatternGeneratorResponse> =>
      request<PatternGeneratorResponse>("/api/users/generate", { method: "POST", body: payload }),
  },

  /**
   * As with OUs, `id` is a hash of the entry's DN, so it changes when the entry is renamed or
   * moved: always work from identifiers taken from a fresh listing. A group's membership is a
   * list of DNs, and an update replaces it wholesale.
   */
  groups: {
    search: (query: PageQuery): Promise<PagedResponse<GroupDto>> =>
      request<PagedResponse<GroupDto>>(`/api/groups${pageQuery(query)}`),
    getById: (id: Uuid): Promise<GroupDto> => request<GroupDto>(`/api/groups/${id}`),
    create: (payload: GroupWriteRequest): Promise<GroupDto> =>
      request<GroupDto>("/api/groups", { method: "POST", body: payload }),
    update: (id: Uuid, payload: GroupWriteRequest): Promise<GroupDto> =>
      request<GroupDto>(`/api/groups/${id}`, { method: "PUT", body: payload }),
    remove: (id: Uuid): Promise<void> => requestNoContent(`/api/groups/${id}`, { method: "DELETE" }),
  },

  orgUnits: {
    search: (query: PageQuery): Promise<PagedResponse<OrganizationalUnitDto>> =>
      request<PagedResponse<OrganizationalUnitDto>>(`/api/organizationalunits${pageQuery(query)}`),
    getById: (id: Uuid): Promise<OrganizationalUnitDto> =>
      request<OrganizationalUnitDto>(`/api/organizationalunits/${id}`),
    create: (payload: OrgUnitWriteRequest): Promise<OrganizationalUnitDto> =>
      request<OrganizationalUnitDto>("/api/organizationalunits", { method: "POST", body: payload }),
    update: (id: Uuid, payload: OrgUnitWriteRequest): Promise<OrganizationalUnitDto> =>
      request<OrganizationalUnitDto>(`/api/organizationalunits/${id}`, { method: "PUT", body: payload }),
    remove: (id: Uuid): Promise<void> =>
      requestNoContent(`/api/organizationalunits/${id}`, { method: "DELETE" }),
  },

  /**
   * Computer accounts, addressed by a hash of the DN as groups and OUs are. What an entry means
   * matters here: this manages directory entries rather than machines — deleting one breaks the
   * machine's domain trust and forces it to rejoin.
   */
  computers: {
    search: (query: PageQuery): Promise<PagedResponse<ComputerDto>> =>
      request<PagedResponse<ComputerDto>>(`/api/computers${pageQuery(query)}`),
    getById: (id: Uuid): Promise<ComputerDto> => request<ComputerDto>(`/api/computers/${id}`),
    create: (payload: ComputerCreateRequest): Promise<ComputerDto> =>
      request<ComputerDto>("/api/computers", { method: "POST", body: payload }),
    update: (id: Uuid, payload: ComputerUpdateRequest): Promise<ComputerDto> =>
      request<ComputerDto>(`/api/computers/${id}`, { method: "PUT", body: payload }),
    remove: (id: Uuid): Promise<void> => requestNoContent(`/api/computers/${id}`, { method: "DELETE" }),
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

# Road Map: LDAP Web Management & REST-Proxy (Open-Core)

## 📌 Этап 1: Архитектура, OpenAPI контракты и Мульти-провайдерность
**Цель:** Создать фундамент системы, способный прозрачно работать с несколькими изолированными или отказоустойчивыми LDAP-серверами.

*   **1.1. Расширение спецификации OpenAPI** (генерируется из кода, доступна на `/swagger`)
    *   Добавление заголовка `X-LDAP-Provider-Id` во все CRUD и поисковые эндпоинты.
    *   Проектирование эндпоинтов управления профилями подключений (`/api/v1/providers`).
    *   Контракты для Bulk-операций (`/api/v1/users/bulk`) и симуляции генерации по маскам.
*   **1.2. Фабрика провайдеров (`ILdapProviderFactory`)**
    *   Реализация динамического разрешения инстанса `ILdapProvider` на основе контекста HTTP-запроса (Tenant / Provider ID).
    *   Реализация логики **Failover**: автоматический опрос пула контроллеров домена (Primary / Replica) и переключение при таймаутах.
*   **1.3. Унификация доменных схем**
    *   Абстракция над различиями атрибутов: `sAMAccountName` (Active Directory) vs `uid` (OpenLDAP).
    *   Построение динамического сборщика уникальных имен (`DN Builder`) под разные типы каталогов.

---

## 🔍 Этап 2: Продвинутый поиск, пагинация и фильтры
**Цель:** Обеспечить стабильную работу интерфейса на базах данных в 100 000+ объектов без просадки по памяти бэкенда.

*   **2.1. Пагинация на базе RFC2696**
    *   Интеграция `SimplePagedResultsControl` в `StandardLdapProvider`.
    *   Безопасная передача LDAP-cookie на фронтенд в виде зашифрованных Base64-токенов (`pageToken`).
*   **2.2. Поисковый движок (Search Engine)**
    *   Конвертер «дружелюбных» поисковых запросов из UI (например, просто ФИО или департамент) в валидные LDAP-фильтры вида `(&(objectClass=user)(|(sn=*...*)(givenName=*...*)))`.
    *   Защита от LDAP Injection (валидация и экранирование входящих строк фильтра).
    *   Реализован `LdapManagementService.SearchUsersAsync` с безопасной генерацией LDAP-фильтра и поддержкой провайдерной фильтрации.
    *   Добавлен endpoint `GET /api/users/search` с параметрами `filter`, `pageSize`, `pageToken`.

---

## ⚡ Этап 3: Модуль массовых операций и автоматизации (Киллер-фичи Open Source)
**Цель:** Дать системным администраторам мощный инструмент для автоматизации рутины без написания скриптов.

*   **3.1. Пакетный процессор (Bulk Processing Engine)**
    *   Реализация эндпоинта массового создания/модификации с поддержкой стандарта `207 Multi-Status` (выдача детального отчета по каждому объекту).
    *   Асинхронная обработка больших пакетов через фоновые задачи (`BackgroundServices` в .NET) с сохранением промежуточного прогресса.
*   **3.2. Генератор объектов по правилам и маскам (Pattern Generator)**
    *   Итератор числовых и буквенных диапазонов (например, `node-{01..99}`).
    *   Regex-парсер сырого текста (из буфера обмена) с поддержкой именованных групп (`(?<firstname>...)`).
    *   Встроенный движок транслитерации (кириллица ➔ латиница) и нормализации регистров для автоматического формирования логинов (`sAMAccountName`).
    *   **Режим симуляции (Preview):** Проверка сгенерированного пула объектов на конфликты в LDAP до физической записи.

---

## 💻 Этап 4: Веб-интерфейс (React + TS + Vite)
**Цель:** Сделать администрирование LDAP интуитивным даже для сотрудников смежных отделов (например, HR).

*   **4.1. Дерево каталога (OU Tree View)**
    *   Интерактивное дерево организационных единиц (OU) с поддержкой ленивой загрузки (Lazy Loading) дочерних узлов.
    *   Функция Drag-and-Drop для быстрого перемещения пользователей и групп между OU (`MoveObject`).
*   **4.2. Мастер массового импорта (Bulk Import Wizard)**
    *   Интерфейс загрузки CSV/JSON-файлов с визуальным маппингом (связыванием) колонок файла с атрибутами LDAP.
    *   Экран генератора по Regex: текстовое поле для сырых данных, поле ввода регулярного выражения и интерактивная таблица предпросмотра результата в реальном времени.

---

## 🛡️ Этап 5: Безопасность, Аудит и Логирование
**Цель:** Защитить конфиденциальные данные учетных записей и обеспечить прозрачность изменений.

*   **5.1. Безопасность секретов**
    *   Запрет на логирование plain-текста паролей при их создании или сбросе (`ChangePassword`).
    *   Шифрование конфигурационных файлов и токенов провайдеров с использованием `AesGcmEncryptionService`.
*   **5.2. Локальный аудит (Audit Trail)**
    *   Запись всех успешных и неуспешных попыток входа и модификации каталога (Кто ➔ Что изменил ➔ У какого объекта).
    *   Генерация детерминированных и неизменяемых ID объектов (`SHA256` от первоначального `DN` ➔ `GUID`).
     - Добавлен локальный аудит: `AuditEvents` таблица, `IAuditService` и `AuditService` в инфраструктуре.
     - Аудит записывает успешные и неуспешные попытки аутентификации и модификации объектов (кто/что/результат/детали).
     - Детеминированные неизменяемые ID: `SHA256(DN)` → `GUID` реализовано (используется при генерации `User.Id` и логах аудита).

---

## 🏢 Этап 6: Enterprise-расширения (Платные модули / Open-Core)
**Цель:** Монетизация проекта для крупного бизнеса за счет фич повышенной безопасности, отказоустойчивости и интеграции.

*   **6.1. Безопасность корпоративного уровня**
    *   Интеграция с внешними хранилищами секретов: HashiCorp Vault, Azure Key Vault, Яндекс Cloud Lockbox.
    *   Модуль двухфакторной аутентификации (MFA: TOTP, WebAuthn) для доступа к веб-панели управления.
    *   Стриминг аудит-логов по протоколу Syslog / CEF в SIEM-системы (Splunk, ELK, ArcSight).
*   **6.2. Identity Governance & Расширенный RBAC**
    *   **OU-level RBAC:** Ограничение прав администраторов рамками конкретных организационных единиц (OU) домена.
    *   **Approval Workflows:** Двухэтапное подтверждение критических операций (например, удаление OU или добавление в группу "Domain Admins" требует аппрува ИБ-директора).
*   **6.3. Автоматизация бизнес-процессов**
    *   HR-синхронизация: Автоматический импорт изменений из внешних систем (1С:ЗУП, SAP, BambooHR) по расписанию.
    *   Password Self-Service: Портал самообслуживания для сотрудников (сброс пароля через SMS/Email-код) и нотификации об истечении срока действия паролей в мессенджеры (Slack, Telegram, Teams).

---

## 🧹 Этап 7: Технический долг и рефакторинг (по итогам аудита)
**Цель:** Устранить накопленные архитектурные и security-риски до того, как они станут проблемой на масштабе (Этапы 4–6).

*   **7.1. Критично — безопасность**
    *   ✅ `appsettings.json`/`appsettings.Development.json` больше не отслеживаются git (`git rm --cached`, файлы остались на диске с рабочими значениями) и добавлены в `.gitignore` (`Nelsar.Axia.Api/appsettings*.json`). Вместо `dotnet user-secrets` выбран подход через `.example`-файлы (не требует Docker/CLI-тулинга, работает одинаково на Windows/Mac/Linux, согласуется с уже существующим `.env.example`): добавлены [appsettings.json.example](Nelsar.Axia.Api/appsettings.json.example) и [appsettings.Development.json.example](Nelsar.Axia.Api/appsettings.Development.json.example) с плейсхолдерами (`CHANGE_ME`) вместо LDAP-пароля/`Jwt:Key`/AES-ключа.
        *   ⚠️ **Всё ещё требует ручных действий, не автоматизируется отсюда:** прежний пароль LDAP admin и прежние `Jwt:Key`/`Encryption:Key` попадали в историю git и должны считаться скомпрометированными — их нужно сменить на стороне AD-сервера и в локальных файлах у каждого разработчика. Также build-копии `Nelsar.Axia.Api/bin/Debug/net10.0/appsettings*.json` всё ещё отслеживаются git (тот же секрет через сборочный вывод) — это уже часть отдельного пункта 7.4 про `bin/`/`obj/` в git, не трогал.
    *   ⏳ Не тронуто: `app.UseHttpsRedirection()` по-прежнему закомментирован в `Program.cs`.
    *   ✅ `[Authorize]` добавлен на `GroupsController` и `OrganizationalUnitsController` — проверено вручную (`curl` без токена → 401 на обоих).
    *   ✅ Добавлен глобальный exception handler (`GlobalExceptionHandler : IExceptionHandler`, зарегистрирован через `AddExceptionHandler`/`UseExceptionHandler`) — возвращает общее сообщение вместо `exception.Message`, реальное исключение логируется на сервере. Убраны дублирующиеся `catch (Exception ex) { return StatusCode(500, new { message = ex.Message }) }` из `UsersController` (Create/Update/Delete); осмысленные `ArgumentException`/`InvalidOperationException` → `Conflict` оставлены как есть.
    *   ✅ Ошибки аудита в `UserService` больше не глотаются молча: добавлен приватный `TryAuditAsync`, логирующий исключение через `_logger.LogWarning` при сбое audit-лога (4 места: `AuthenticateAsync` x2, `CreateAsync`, `UpdateAsync`), при этом сам запрос пользователя по-прежнему не падает из-за проблем с аудитом.
*   **7.2. Слоение / архитектура**
    *   ✅ Вынесена генерация JWT из `UserService` (Application) в Infrastructure за интерфейсом `ITokenService` (`Application/Interfaces/ITokenService.cs` + `Infrastructure/Identity/JwtTokenService.cs`) — Application больше не зависит от `System.IdentityModel.Tokens.Jwt` (пакет удалён из `Nelsar.Axia.Application.csproj`).
    *   ✅ `LdapConfiguration`/`LdapAttributeMapping` перенесены из `Application/Configuration` в `Domain/Entities`, как остальные персистентные сущности (`Provider`, `RoleMapping`, `AuditEvent`). Проверено, что перенос не требует изменения схемы БД: сгенерированная EF-миграция `RenameLdapConfigurationNamespace` пустая (`Up`/`Down` без операций), обновился только снапшот модели.
    *   ✅ Статический `ConcurrentDictionary<Guid, UserDto> UserCache` в `UserService` заменён на инстансный `IMemoryCache` (DI, singleton) со sliding expiration 30 минут — убран риск неограниченного роста памяти и кросс-тестового state leak; `Microsoft.Extensions.Caching.Memory` подключён к Application (версия 8.0.1, без известных уязвимостей). Кэш остаётся процесс-локальным — для мультиинстансного деплоя всё ещё нужен распределённый кэш (см. открытый пункт про горизонтальное масштабирование ниже).
    *   ✅ `DependencyInjection.AddInfrastructureServices` разбит на `AddPersistence()`, `AddLdapIdentity()`, `AddApplicationServices()`, `AddBackgroundJobs()`, `AddAuthenticationScheme()` — каждый со своей зоной ответственности вместо одного метода на ~100 строк.
    *   ⏳ **Открытое решение (не тронуто, требует явного продуктового решения):** стратегия хранения bulk-джобов. Сейчас `IBulkJobQueue`/`IBulkJobStore` — in-memory Singleton, тогда как `Providers`/`RoleMappings`/`RateLimitSettings` уже в SQLite. Перевод на БД-очередь — это новая сущность + репозиторий + миграция, а не рефакторинг существующего кода, поэтому не выполнялось без подтверждения.
    *   ⏳ **Открытое решение (не тронуто):** судьба SQLite (остаётся для однопроцессного деплоя или переход на PostgreSQL/SQL Server перед мультиинстансным продом) — это инфраструктурное решение вне рамок рефакторинга кода.
*   **7.3. Дублирование кода**
    *   ✅ `EscapeLdapFilterValue` вынесен в общий `Application/Common/LdapFilterEscaper.cs`, используется и в `LdapService` (Infrastructure), и в `LdapManagementService` (Application) — фикс LDAP-инъекции теперь в одном месте.
    *   ✅ `BuildDeterministicUserId` вынесен в `Application/Common/DeterministicIdFactory.cs`, используется в `UserService` и `LdapManagementService`. Заодно устранена мелкая несогласованность: `LdapManagementService.MapUser` раньше при отсутствии email/DN откатывался на случайный `Guid.NewGuid()` (недетерминированно), теперь как и `UserService` использует `DisplayName` как третий уровень фолбэка перед случайным значением.
    *   ⚠️ `UserService.GetByIdAsync` — точечный поиск по Id в LDAP невозможен в принципе: `Id` это SHA256(email|dn), а не атрибут LDAP, по которому можно фильтровать. На cache miss код обязан перебрать каталог и пересчитать Id для сравнения; `GetAllAsync()` возвращает ленивый `IEnumerable`, поэтому `FirstOrDefault` уже останавливается на первом совпадении — код честно прокомментирован (см. `UserService.cs`). Настоящий point-lookup потребовал бы отдельного персистентного индекса Id → email/DN — не делал это без вашего решения, см. открытый пункт про bulk-джобы выше (аналогичный по духу компромисс).
*   **7.4. Гигиена репозитория**
    *   ⏳ Не тронуто: `.gitignore` для `bin/`/`obj/`/`*.user` и очистка уже закоммиченных артефактов сборки и `.venv`/`.venv-1` — требует явного запроса (потенциально деструктивная git-операция).
    *   ✅ Версии NuGet-пакетов выровнены под `net10.0`: `Microsoft.EntityFrameworkCore*`, `Microsoft.AspNetCore.Authentication.JwtBearer`, `Microsoft.Extensions.*`, `System.DirectoryServices*` → `10.0.10`; `System.IdentityModel.Tokens.Jwt`/`Microsoft.IdentityModel.Tokens` → `8.21.0`. Побочно закрыта известная уязвимость транзитивного `SQLitePCLRaw.lib.e_sqlite3` (закреплён на `2.1.12` через `SQLitePCLRaw.bundle_e_sqlite3`). EF Core 8→10 подтверждённо не меняет схему БД (пустая проверочная миграция `EfCore10AlignmentCheck`). Все 90 тестов проходят.
        *   ⏳ **Не тронуто (откачено после попытки):** `Swashbuckle.AspNetCore` 6.6.2→10.2.3 и `Novell.Directory.Ldap.NETStandard` 3.6.0→4.0.0 — оба оказались настоящими breaking-change мажорными апгрейдами (переезд на OpenApi.NET v2 с новым namespace/API схемы; переход LDAP-клиента на async-only API, затрагивает `LdapConnectionFactory`/`LdapService`/`LdapUserMapper`/`StandardLdapProvider`/`DefaultLdapProviderFactory`), а не просто bump версии. LDAP-клиент к тому же не покрыт тестами на уровне реального wire-протокола (тесты мокают на границе `ILdapProvider`) — миграцию вслепую делать не стал, нужна отдельная сессия с ревью и, желательно, реальным LDAP-сервером для проверки.
    *   ✅ `GroupsController`/`OrganizationalUnitsController`/`ILdapManagementService` (Group/OU-заглушки) явно помечены как `EXPERIMENTAL / STUB` через XML-doc комментарии на контроллерах и интерфейсе; включена генерация XML-документации (`GenerateDocumentationFile`) и её подключение в Swagger (`IncludeXmlComments`), так что пометки видны прямо в Swagger UI, а не только в исходниках. *(Запись этапа: OU и группы с тех пор реализованы — см. последние пункты changelog в конце файла; пометок `EXPERIMENTAL / STUB` в коде больше нет, генерация XML-доков в Swagger осталась.)*
    *   ✅ **Решено (подтверждено владельцем продукта):** `UsersController.Search`/`GetById` намеренно доступны любому аутентифицированному пользователю без ограничения по роли — в отличие от `GetAll`/`Create`/`Update`/`Delete`. Изменений не требуется.

---

## ✅ Что уже реализовано
*  Формализован API-спецификатор: генерируется Swagger из кода и отдаётся на `/swagger`.
*  Добавлены доменные модели и DTO для групп и организационных подразделений.
*  Добавлен `ILdapManagementService` и тестовый сервис `LdapManagementService`.
*  Создан проект `Nelsar.Axia.Tests`, добавлены unit-тесты для `UserService` и `LdapManagementService`.
*  Реализован поисковый движок `LdapManagementService.SearchUsersAsync` с безопасным генератором LDAP-фильтров и endpoint `GET /api/users/search`.
*  Успешно пройден `dotnet test` для нового тестового проекта.
*  **Интеграционные тесты** (.NET 10 compatible):
   - Внедрены 8 интеграционных тестов через `WebApplicationFactory` для `UsersController` и `AuthController`.
   - Покрыты все CRUD операции: GetAll, GetById, Create, Update, Delete.
   - Покрыты оба endpoint'а AuthController: Login (valid & invalid), Register.
   - 4 unit-теста для `UserService` (auth, create, getAll, getById).
   - 2 unit-теста для `LdapManagementService` (paging).
   - **Всего 16 тестов**, все успешно проходят.
    Исправлены ошибки совместимости .NET 10: PipeWriter и DbInitializer.
    - Реализовано шифрование конфигурационных секретов: `AesGcmEncryptionService` с форматом `enc:<nonce>:<cipher>:<tag>`; `DbInitializer` теперь сохраняет `EncryptedBindPassword` в зашифрованном виде при наличии `Encryption:Key`.
    - Запрет логирования plain-text паролей при создании/сбросе паролей: сервисы и провайдеры не выводят пароли в логах (маскируются/не логируются).
*  Реализована поддержка заголовка `X-LDAP-Provider-Id` в HTTP-пайплайне (middleware) и добавлен фильтр OpenAPI для документации этого заголовка.
*  Добавлен scaffold API для управления профилями провайдеров: `/api/v1/providers` (CRUD) с ин-мемори сервисом `IProviderService`.
*  `IProviderService` переведен с in-memory реализации на EF-репозиторий `ProviderRepository`; добавлена сущность `Provider`, таблица `Providers` и SQL-миграция для схемы.
*  **Восстановлена авторизация в `UsersController`**: `[Authorize]`/`[Authorize(Roles=...)]` раскомментированы на всех CRUD-эндпоинтах; `ProvidersController` также закрыт ролью `Admin`, так как теперь хранит LDAP bind-креды. Попутно исправлена скрытая ошибка в `DependencyInjection.cs` — `JwtBearerOptions` привязывались к снапшоту конфигурации на момент регистрации DI, а не резолвились лениво через `IOptions<JwtSettings>`, из-за чего валидация токена не подхватывала конфигурацию хоста (включая тестовые/reload-сценарии). Добавлены интеграционные тесты на 401/403.
*  **`ILdapProviderFactory` подключена к БД**: `Provider` расширен полями `BaseDn`, `AdminUsername`, `EncryptedAdminPassword` (AES-GCM), `UserSearchFilter`, `UserSearchPageSize`, `FailoverHosts`; фабрика сначала резолвит провайдера по `Guid` из `ProviderRepository`, при неудаче падает на конфиг-секцию `Ldap:Providers:{id}`, затем на дефолтный провайдер. Пароль в `ProviderDto` никогда не возвращается. Миграция `AddProviderLdapConnectionDetails` аддитивна (безопасна для существующих строк).
*  **Bulk-эндпоинт с `207 Multi-Status`**: `POST /api/users/bulk/sync` обрабатывает пакет синхронно и возвращает `BulkOperationResponse` с детальным отчётом по каждому объекту через HTTP 207; существующий `POST /api/users/bulk` (async job + polling) сохранён для больших пакетов.
*  **Pattern Generator доведён до заявленного**: добавлен regex-парсер сырого текста с именованными группами (`RawText`/`RegexPattern`/`UsernameTemplate`), транслитерация кириллица→латиница (включая казахские буквы), и режим `CheckConflicts` — проверка сгенерированных логинов на конфликты в LDAP до записи (preview/simulation).
*  **Динамический маппинг ролей**: жёстко зашитое соответствие AD-групп → роль вынесено в таблицу `RoleMappings` с CRUD через `/api/v1/role-mappings` (`Admin`-only). Поддержаны приоритеты (при членстве в нескольких группах побеждает `Priority` с наименьшим значением) и скоуп на конкретный домен через `Provider.Id` (mapping с заданным `ProviderId` перекрывает глобальный для той же группы). `LdapService.GetUserRoleAsync` теперь резолвит роль через `IRoleMappingResolver`, доставая текущий `ProviderId` из `X-LDAP-Provider-Id`. При первом запуске сидируются 4 дефолтные глобальные записи, воспроизводящие старое поведение.
*  **Настраиваемый rate limiting**: добавлен `Microsoft.AspNetCore.RateLimiting` с политикой `auth-login` (fixed window, партиционирование по IP клиента) на `/api/auth/login` и `/api/auth/register`. Лимиты (`PermitLimit`/`WindowSeconds`/`QueueLimit`/`Enabled`) хранятся в таблице `RateLimitSettings`, редактируются через `/api/v1/rate-limit-settings` (`Admin`-only) и подхватываются без рестарта — партиционер читает их через `IRateLimitSettingsProvider` с 30-секундным кэшем поверх БД. Дефолт при отсутствии записи (в т.ч. в тестовом окружении, где сид не запускается) — 5 запросов/60с.
*  **Мульти-доменные интеграционные тесты** (`MultiDomainIntegrationTests`, 6 тестов): один API-инстанс обслуживает два домена — AD-стиль (CN= DN, sAMAccountName, группа "Domain Admins") и OpenLDAP-стиль (uid= DN, groupOfNames) — с переключением через `X-LDAP-Provider-Id`. Каталоги эмулируются на границе `ILdapProvider` (живых AD/OpenLDAP в тестовом прогоне нет), весь остальной конвейер настоящий: middleware заголовка, роутинг провайдера per-request, `LdapService`, `RoleMappingResolver` (проверяется, что provider-scoped маппинг "Domain Admins"→Admin действует только в AD-домене и не «протекает» в OpenLDAP-домен), `UserService`, JWT, контроллеры.
*  **`StandardLdapProvider` исправлен для реальной кроссплатформенности**: `GetAllUsers()` использовал AD-специфичный фильтр `objectCategory=person` (такого атрибута нет в OpenLDAP/eDirectory — листинг молча возвращал пусто); заменён на портируемый `(objectClass=person)`, который совпадает с полной цепочкой классов у AD/OpenLDAP/eDirectory одинаково. `Authenticate()` больше не гадает DN по шаблону `uid=...` (не работает для AD, где RDN обычно `CN=`) — теперь сперва ищет пользователя (как и остальные методы), затем биндится найденным DN. Дефолтный `UserSearchFilter` (в `LdapSettings` и в `Provider`) заменён с AD-only `sAMAccountName={0}` на комбинированный `(|(uid={0})(sAMAccountName={0})(userPrincipalName={0})(mail={0}))`, покрывающий все три схемы одним запросом; резолв username в `MapEntry` тоже расширен на `sAMAccountName`/`userPrincipalName`. `CreateUser` осознанно оставлен OpenLDAP/eDirectory-специфичным (uid-RDN, inetOrgPerson) — создание настоящих AD-объектов через generic LDAP требует другого набора атрибутов (`userAccountControl`, `unicodePwd` и т.д.); для записи в AD использовать `ActiveDirectoryProvider`.
*  **Устранены проблемы слоения из аудита (Этап 7.2)**: генерация JWT вынесена из `UserService` в `Infrastructure/Identity/JwtTokenService` за интерфейсом `ITokenService` (Application больше не зависит от `System.IdentityModel.Tokens.Jwt`); `LdapConfiguration`/`LdapAttributeMapping` перенесены из `Application/Configuration` в `Domain/Entities` (EF-миграция `RenameLdapConfigurationNamespace` подтверждает отсутствие изменений схемы БД — `Up`/`Down` пустые); статический `ConcurrentDictionary UserCache` в `UserService` заменён на DI-managed `IMemoryCache` со sliding expiration 30 мин; `DependencyInjection.AddInfrastructureServices` разбит на `AddPersistence`/`AddLdapIdentity`/`AddApplicationServices`/`AddBackgroundJobs`/`AddAuthenticationScheme`. Все 90 тестов проходят после рефакторинга. Стратегия хранения bulk-джобов (in-memory vs БД) и выбор БД-движка (SQLite vs PostgreSQL/SQL Server) оставлены открытыми как продуктовые решения — см. пометки ⏳ в 7.2.
*  **Устранено дублирование кода и версии пакетов приведены к `net10.0` (Этап 7.3/7.4)**: `EscapeLdapFilterValue` и `BuildDeterministicUserId` вынесены в `Application/Common/LdapFilterEscaper.cs` и `Application/Common/DeterministicIdFactory.cs`, используются из `LdapService` и `LdapManagementService` вместо двух независимых копий; `GroupsController`/`OrganizationalUnitsController`/`ILdapManagementService`-заглушки помечены `EXPERIMENTAL / STUB` через XML-doc, видимые в Swagger UI (`GenerateDocumentationFile` + `IncludeXmlComments`) *(запись этапа: и OU, и группы с тех пор реализованы — см. последние пункты changelog в конце файла; пометки сняты)*; `Microsoft.EntityFrameworkCore*`, `Microsoft.AspNetCore.Authentication.JwtBearer`, `Microsoft.Extensions.*`, `System.DirectoryServices*` обновлены до `10.0.10`, `System.IdentityModel.Tokens.Jwt` до `8.21.0`, попутно закрыта уязвимость `SQLitePCLRaw.lib.e_sqlite3` (закреплён `2.1.12`). `Swashbuckle.AspNetCore` (→10.x, редизайн OpenApi.NET v2) и `Novell.Directory.Ldap.NETStandard` (→4.x, async-only API) сознательно откачены после пробного апгрейда — оба требуют полноценной миграции кода, а не bump версии, и LDAP-клиент к тому же не покрыт тестами на уровне реального wire-протокола. Все 90 тестов проходят.
*  **`[Authorize]` на `GroupsController`/`OrganizationalUnitsController`, глобальный exception handler, audit-логирование не глотается молча**: добавлен `GlobalExceptionHandler : IExceptionHandler` ([Middleware/GlobalExceptionHandler.cs](Nelsar.Axia.Api/Middleware/GlobalExceptionHandler.cs)), убраны дублирующиеся `catch (Exception ex) { return StatusCode(500, new { message = ex.Message }) }` из `UsersController`; `UserService` получил приватный `TryAuditAsync`, логирующий сбой аудита вместо пустого `catch {}`. Проверено живым прогоном (`curl` без токена → 401 на Groups/OrgUnits).
*  **Секреты выведены из-под git-трекинга**: `appsettings.json`/`appsettings.Development.json` сняты с учёта (`git rm --cached`, файлы остаются на диске рабочими), добавлены `appsettings.json.example`/`appsettings.Development.json.example` с плейсхолдерами, `.gitignore` дополнен `Nelsar.Axia.Api/appsettings*.json`. Реальные LDAP-пароль/`Jwt:Key`/`Encryption:Key` уже есть в истории git — считать скомпрометированными, ротация вне зоны досягаемости отсюда (нужен доступ к самому AD-серверу).
*  **Повторный аудит (2026-07-26) нашёл и закрыл 5 новых проблем**, не связанных с предыдущими раундами:
   - `BulkUserBackgroundService` писал `ex.Message` в `BulkUserJob.ErrorMessage`, который отдаётся клиенту через `GetBulkJobStatusAsync` — тот же класс утечки, что чинился в контроллерах, но не через HTTP-пайплайн, поэтому `GlobalExceptionHandler` его не перехватывал. Заменено на generic-сообщение, полное исключение остаётся в логах.
   - `InMemoryBulkJobStore` был `ConcurrentDictionary` без эвикции — растущая вечно память на долгоживущем процессе. Переведён на `IMemoryCache` со sliding expiration 2 часа (тот же паттерн, что уже применялся к `UserService.UserCache`).
   - `DefaultLdapProviderFactory`: `TryConnect`'s `timeoutMs` объявлялся, но никогда не применялся к `LdapConnection.ConnectionTimeout` — недоступный failover-хост мог висеть намного дольше заявленных 2с. Исправлено. Плюс добавлено 30-секундное кэширование результата `Resolve(providerId)` через `IMemoryCache` — снижает и частоту sync-blocking DB-запроса (`.GetAwaiter().GetResult()`), и частоту TCP-проб доступности хоста на каждый запрос с `X-LDAP-Provider-Id`. Полный перевод `ILdapProviderFactory`/`ILdapService` на async — более крупный, отдельный рефакторинг, не делался.
   - Audit trail был write-only: `IAuditRepository`/`IAuditService` не имели ни одного метода чтения, `AuditController` отсутствовал. Добавлены `IAuditRepository.GetRecentAsync`/`IAuditService.GetRecentAsync` (offset-пагинация через новый общий `Application/Common/OffsetPageToken.cs`, которым заодно заменена такая же дублированная логика в `LdapManagementService`), `AuditEventDto`, и `GET /api/v1/audit-events` (`Admin`-only) в новом [AuditController.cs](Nelsar.Axia.Api/Controllers/AuditController.cs). При живом прогоне поймал реальный баг: SQLite-провайдер EF Core не транслирует `ORDER BY` по `DateTimeOffset` на сервере (`AuditEvent.Timestamp`) — юнит-тесты (in-memory DB) этого не ловят, только реальный SQLite. Пофикшено сортировкой/пагинацией на клиенте, как уже сделано в `LdapManagementService` для LDAP-результатов. Проверено сквозным вызовом (сгенерированный JWT → `POST /api/auth/login` → `GET /api/v1/audit-events` → событие в ответе, отсортировано по убыванию времени).
   - `NoOpEncryptionService` был мёртвым кодом (`[Obsolete]`, нигде не зарегистрирован в DI), при этом `LdapService.cs` содержал протухший `// TODO: replace NoOpEncryptionService...`, будто он всё ещё используется. Класс удалён, комментарий убран.
   Все 90 тестов проходят; сборка чистая (кроме двух ранее существовавших warning'ов, не связанных с этой правкой).
*  **Управление группами реализовано (`/api/groups` больше не заглушка)**: `LdapManagementService` получил настоящую реализацию `SearchGroupsAsync`/`GetGroupAsync`/`CreateGroupAsync`/`UpdateGroupAsync`/`DeleteGroupAsync` поверх новых портов `ILdapService.GetGroupsAsync`/`CreateGroupAsync`/`UpdateGroupAsync`/`DeleteGroupAsync` и `ILdapProvider.GetAllGroups`/`CreateGroup`/`UpdateGroup`/`DeleteGroup` — ровно по той же схеме, что OU: `id` — хэш от DN (`DeterministicIdFactory.BuildDirectoryObjectId`), фильтрация в памяти по имени/DN/описанию, пагинация через `OffsetPageToken`, `IsComplete` из `DirectoryGroupListing`; промах по id в оборвавшемся переборе даёт `DirectoryUnavailableException` (502), а не 404. В `StandardLdapProvider` добавлены чтение (`(|(objectClass=group)(objectClass=groupOfNames)(objectClass=groupOfUniqueNames))`, `member`/`uniqueMember`; `posixGroup` осознанно исключён — его `memberUid` хранит логины, а не DN, и любая правка состава ушла бы не в тот атрибут) и запись: create пишет `groupOfNames`, rename/move — `moddn` с `deleteOldRdn: true`, состав заменяется целиком. Пустой состав отклоняется на входе (`DomainValidationException` → 400) с объяснением: по RFC 4519 `member` обязателен для `groupOfNames`, поэтому пустую группу каталог не сохранит, и внятное сообщение полезнее серверного object-class violation. `ActiveDirectoryProvider` группами не управляет и говорит это через `FeatureNotImplementedException` → 501 (как и с OU: `GroupPrincipal` не покрывает rename/move, это был бы второй Windows-only путь записи, недостижимый ни для одного теста здесь) — создание настоящих AD security-групп (SID, `groupType`) остаётся вне API. Попутно исправлен тот же баг, что был у OU: `GroupsController.GetAll` не объявлял параметр `filter` и передавал `null`, поэтому `?filter=` принимался, молча терялся и в ответ уходил весь каталог. `UpdateGroupRequest` переведён на nullable-семантику частичного обновления (null = «не трогать»), иначе запрос с одним новым именем опустошал бы состав группы и стирал описание. Удалена мёртвая сущность `Domain/Entities/Group.cs` (ни `DbSet`, ни одной ссылки) — рядом с новым `LdapGroup` она читалась бы как модель групп. Экран «Группы» во фронтенде из заглушки превращён в полноценный CRUD (поиск, создание, переименование/перенос, правка состава, удаление, пометка неполного перебора). Тесты: `NotImplementedEndpointsTests` удалён (нечего больше пиннить), вместо него `GroupTests` — 21 тест, включая сквозные на `/api/groups`, на применение `?filter=` и на 501 от провайдера, который группами не управляет. Все 278 тестов проходят.

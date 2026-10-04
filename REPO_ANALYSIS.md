# Обзор проекта Nelsar.Axia

**Дата:** 2026-07-12

## Краткий обзор

- Решение разделено на проекты: `Nelsar.Axia.Api`, `Nelsar.Axia.Application`, `Nelsar.Axia.Domain`, `Nelsar.Axia.AxInfrastructure`.
- HTTP API: контроллеры для аутентификации и управления пользователями.
- Аутентификация реализована через LDAP + JWT, провайдер LDAP выбирается через конфигурацию.

## Структура и ключевые файлы

- API контроллеры:
  - [Nelsar.Axia.Api/Controllers/AuthController.cs](Nelsar.Axia.Api/Controllers/AuthController.cs)
  - [Nelsar.Axia.Api/Controllers/UsersController.cs](Nelsar.Axia.Api/Controllers/UsersController.cs)
- Сервис пользователя:
  - [Nelsar.Axia.Application/Services/UserService.cs](Nelsar.Axia.Application/Services/UserService.cs)
- LDAP / инфраструктура:
  - [Nelsar.Axia.Infrastructure/Identity/LdapService.cs](Nelsar.Axia.Infrastructure/Identity/LdapService.cs)
  - [Nelsar.Axia.Infrastructure/Identity/LdapUserMapper.cs](Nelsar.Axia.Infrastructure/Identity/LdapUserMapper.cs)
  - [Nelsar.Axia.Infrastructure/DependencyInjection.cs](Nelsar.Axia.Infrastructure/DependencyInjection.cs)

## Что сделано (функционально)

- API предоставляет регистрация, логин (JWT) и CRUD операций для пользователей.
- `UserService` взаимодействует с `ILdapService` для аутентификации и управления пользователями.
- Пользовательские DTO кешируются в памяти (`ConcurrentDictionary<Guid, UserDto>`).
- В DI настроена политика аутентификации: JWT + PolicyScheme, который переключается на Basic (LDAP) при заголовке `Authorization: Basic ...`.
- Есть маппер LDAP-атрибутов и поддержка разных провайдеров (`ActiveDirectoryProvider` / `StandardLdapProvider`).

## Технические наблюдения

- Атрибуты `[Authorize]` в контроллерах закомментированы — операции в текущей кодовой базе не защищены.
- `GetAllUsersAsync` возвращает всех пользователей без пагинации — риск `Size Limit Exceeded` на больших LDAP-поддеревьях.
- В коде применяется `IEncryptionService.Decrypt` для `EncryptedBindPassword`, но в коде есть упоминание `NoOpEncryptionService` — проверить реальную реализацию в окружении.
- Идентификаторы пользователей строятся детерминированно из SHA256 исходной строки (16 байт) — рабочее, но стоит документировать поведение.
- Кеширование в памяти хорошее для быстрого доступа, но неустойчиво при перезапуске; `GetById` при проксте ресарта делает полный LDAP-скан.
- JWT секрет используется напрямую при создании `SymmetricSecurityKey` — убедитесь в безопасном хранении ключа и механизме ротации.

## Рекомендации

1. Включить и протестировать авторизацию: восстановить `[Authorize]`, проверить JWT + Basic переключение.
2. Проверить/заменить `NoOpEncryptionService` — обеспечить реальное шифрование секретов в проде.
3. Реализовать постраничную выборку LDAP (RFC2696 SimplePagedResultsControl) или серверную пагинацию для `GetAllUsersAsync`.
4. Рассмотреть постоянное хранилище для пользователей (или быстрый индекс), чтобы `GetById` не требовал полного перебора LDAP после перезапуска.
5. Переместить `JwtSettings.Key` и другие секреты в vault/secret manager и документировать ротацию ключей.
6. Добавить юнит и интеграционные тесты для `UserService`, `LdapService` и LDAP-провайдеров (мокать соединения).
7. Проверить процесс создания пароля в LDAP: не записывать открытые пароли в логи; убедиться, что LDAP-хост сам хеширует пароли или использовать подходящие атрибуты.

## Следующие шаги (варианты)

- Включить авторизацию и прогнать ручные запросы (Postman).
- Добавить пример реализации пагинации для LDAP.
- Проверить, где используется `NoOpEncryptionService` и заменить на реальную реализацию.

Если хотите — выполню один из пунктов автоматически. Скажите, что именно.
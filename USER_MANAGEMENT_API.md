# Nelsar.Axia User Management API с LDAP и JWT Авторизацией

## DDD Architecture Structure

### 📁 Domain Layer (`Nelsar.Axia.Domain`)
- **Entities/User.cs** - Core domain entity (без привязки к LDAP)

### 📁 Application Layer (`Nelsar.Axia.Application`)
- **Configuration/**
  - JwtSettings.cs - JWT конфигурация
  - LdapSettings.cs - LDAP конфигурация
  - UserRoles.cs - Константы ролей
- **DTOs/** - Data Transfer Objects
  - UserDto, UserCreateRequest, UserUpdateRequest
  - AuthenticateRequest, AuthenticateResponse
- **Interfaces/**
  - IUserService - управление пользователями
  - ILdapService - работа с LDAP
- **Services/**
  - UserService - реализация с использованием LDAP
  - LdapService - интеграция с Active Directory

### 📁 Infrastructure Layer (`Nelsar.Axia.Infrastructure`)
- **DependencyInjection.cs** - Регистрация сервисов
  - JWT аутентификация
  - LDAP конфигурация
  - IUserService и ILdapService

### 📁 API Layer (`Nelsar.Axia.Api`)
- **Controllers/**
  - AuthController.cs - Логин (открытый) и регистрация
  - UsersController.cs - CRUD с ролевыми проверками
- **Program.cs** - Application bootstrap

---

## 🔐 Роли и Авторизация

### Доступные роли:
- **Admin** - Полный доступ (создание, редактирование, удаление)
- **UserCreator** - Создание пользователей
- **UserEditor** - Редактирование пользователей
- **UserDeleter** - Удаление пользователей
- **User** - Базовый пользователь (только просмотр своего профиля)

### Ролевые проверки:
- **GET /api/users** - Требует: Admin или UserCreator
- **POST /api/users** - Требует: Admin или UserCreator
- **PUT /api/users/{id}** - Требует: Admin или UserEditor
- **DELETE /api/users/{id}** - Требует: Admin или UserDeleter

Роли назначаются из AD групп:
- Группа "Administrators" → Role = Admin
- Группа "UserCreators" → Role = UserCreator
- Группа "UserEditors" → Role = UserEditor
- Группа "UserDeleters" → Role = UserDeleter

---

## API Endpoints

### Аутентификация (Открытые)

#### Логин (получить JWT токен)
```
POST /api/auth/login
Content-Type: application/json

{
  "email": "user@example.com",
  "password": "Password123!"
}

Response: 200 OK
{
  "token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "user": {
    "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "email": "user@example.com",
    "name": "John Doe",
    "role": "User"
  }
}
```

#### Регистрация пользователя (требует UserCreator)
```
POST /api/auth/register
Content-Type: application/json

{
  "email": "newuser@example.com",
  "name": "Jane Doe",
  "password": "SecurePass123!"
}

Response: 201 Created
{
  "id": "guid",
  "email": "newuser@example.com",
  "name": "Jane Doe",
  "role": "User"
}
```

### Управление пользователями (Защищено JWT)

В все запросах добавить заголовок:
```
Authorization: Bearer <jwt_token>
```

#### Получить всех пользователей
```
GET /api/users
Authorization: Bearer <token>

Требует роль: Admin, UserCreator
Response: 200 OK
[
  {
    "id": "guid",
    "email": "user@example.com",
    "name": "John Doe",
    "role": "Admin"
  }
]
```

#### Получить пользователя по ID
```
GET /api/users/{id}
Authorization: Bearer <token>

Response: 200 OK
{
  "id": "guid",
  "email": "user@example.com",
  "name": "John Doe",
  "role": "User"
}
```

#### Обновить пользователя
```
PUT /api/users/{id}
Authorization: Bearer <token>
Content-Type: application/json

Требует роль: Admin, UserEditor

{
  "name": "Jane Smith",
  "email": "jane@example.com",
  "password": "NewPassword123!"
}

Response: 200 OK
{
  "id": "guid",
  "email": "jane@example.com",
  "name": "Jane Smith",
  "role": "User"
}
```

#### Удалить пользователя
```
DELETE /api/users/{id}
Authorization: Bearer <token>

Требует роль: Admin, UserDeleter
Response: 204 No Content
```

---

## Конфигурация

### appsettings.json

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "Ldap": {
    "Server": "ldap.yourdomain.com",
    "Port": 389,
    "BaseDn": "dc=example,dc=com",
    "AdminUsername": "admin@yourdomain.com",
    "AdminPassword": "AdminPassword123!",
    "UserSearchFilter": "(&(objectClass=person)(sAMAccountName={0}))"
  },
  "Jwt": {
    "Key": "CHANGE_ME_AT_LEAST_32_CHARACTERS_LONG",
    "Issuer": "Nelsar.Axia.Api",
    "Audience": "Nelsar.Axia.Api",
    "ExpiresInMinutes": 60
  },
  "AllowedHosts": "*"
}
```

---

## Настройка AD групп

Для работы ролевой авторизации создайте в Active Directory следующие группы:

1. **Administrators** - Административный доступ
2. **UserCreators** - Создание пользователей
3. **UserEditors** - Редактирование пользователей
4. **UserDeleters** - Удаление пользователей

Добавьте нужных пользователей в соответствующие группы.

---

## Примеры использования

### Пример 1: Логин и получение токена
```bash
curl -X POST http://localhost:5000/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{
    "email": "admin@yourdomain.com",
    "password": "password123"
  }'
```

### Пример 2: Получить список пользователей (с токеном)
```bash
curl -X GET http://localhost:5000/api/users \
  -H "Authorization: Bearer eyJhbGciOiJIUzI1NiIs..."
```

### Пример 3: Создать нового пользователя
```bash
curl -X POST http://localhost:5000/api/users \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer eyJhbGciOiJIUzI1NiIs..." \
  -d '{
    "email": "newuser@yourdomain.com",
    "name": "New User",
    "password": "SecurePass123!"
  }'
```

---

## Зависимости проекта

### Application Layer (Nelsar.Axia.Application)
- `Microsoft.Extensions.Options.ConfigurationExtensions` - Конфигурация
- `Microsoft.Extensions.Logging` - Логирование
- `System.IdentityModel.Tokens.Jwt` - JWT токены
- `System.DirectoryServices` - LDAP/AD
- `System.DirectoryServices.AccountManagement` - AD Principal API

### Infrastructure Layer (Nelsar.Axia.Infrastructure)
- `Microsoft.AspNetCore.Authentication.JwtBearer` - JWT Bearer auth
- Все зависимости из Application

### API Layer (Nelsar.Axia.Api)
- Все зависимости из Infrastructure

---

## Build Status

✅ **Build**: Success (0 errors, ~90 platform warnings для LDAP/AD на non-Windows)

**Примечание**: Warnings о Windows-only APIs нормальны для LDAP/Active Directory функциональности.

---

## Будущие улучшения

- [ ] Refresh token механизм
- [ ] Двухфакторная аутентификация (2FA)
- [ ] Аудит логи всех операций
- [ ] Rate limiting на endpoints
- [ ] Кэширование ролей пользователей
- [ ] Поддержка других провайдеров авторизации (OAuth2, OIDC)
- [ ] SQL Server для хранения аудит логов

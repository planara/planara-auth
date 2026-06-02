![build](https://github.com/planara/planara-auth/actions/workflows/build.yml/badge.svg)
![release](https://github.com/planara/planara-auth/actions/workflows/release.yml/badge.svg)
![publish-k3s](https://github.com/planara/planara-auth/actions/workflows/publish-k3s.yml/badge.svg?branch=main)
![version](https://img.shields.io/github/v/tag/planara/planara-auth?sort=semver)
[![Codecov](https://codecov.io/gh/planara/planara-auth/branch/main/graph/badge.svg)](https://codecov.io/gh/planara/planara-auth)
[![License: MIT](https://img.shields.io/badge/License-MIT-green.svg)](https://opensource.org/licenses/MIT)
[![PRs Welcome](https://img.shields.io/badge/PRs-welcome-brightgreen.svg)](http://makeapullrequest.com)

## Planara.Auth

Сервис аутентификации и авторизации.

Отвечает за регистрацию пользователей, вход, обновление access токенов,
управление refresh токенами, выход из аккаунта и удаление аккаунта пользователя.

Реализован как ASP.NET Core + GraphQL сервис с JWT access токенами,
refresh токенами с ротацией и outbox-публикацией событий в Kafka.

## Возможности

* Регистрация пользователей
* Вход по email / паролю
* JWT access tokens
* Refresh tokens с ротацией
* Logout с отзывом refresh токена
* Удаление аккаунта текущего пользователя
* Публикация события создания пользователя в Kafka
* Публикация события удаления пользователя в Kafka
* Outbox pattern для надежной доставки событий
* JWT авторизация (`[Authorize]`)
* Валидация входных данных (FluentValidation)
* GraphQL API (HotChocolate)

## GraphQL API

### Queries

* `me: UUID`
  Возвращает ID текущего пользователя
  *(требует авторизации)*

### Mutations

* `register(request: RegisterRequestInput): AuthResponse`
  Регистрирует пользователя и выдает пару access / refresh токенов

* `login(login: LoginRequestInput): AuthResponse`
  Выполняет вход по email и паролю

* `refresh(request: RefreshRequestInput): AuthResponse`
  Обновляет access токен по refresh токену и выполняет ротацию refresh токена

* `logout(request: LogoutRequestInput): LogoutResponse`
  Отзывает refresh токен пользователя

* `deleteAccount: DeleteAccountResponse`
  Удаляет аккаунт текущего пользователя
  *(требует авторизации)*

## Запуск

Перед запуском сервиса необходимо поднять инфраструктуру через Docker Compose.

```bash
docker compose up -d
```

После запуска инфраструктуры можно запустить сервис:

```bash
dotnet run --project src/Planara.Auth.csproj
```

GraphQL endpoint:

```text
/graphql
```

## Тестирование

Для запуска тестов требуется Docker, так как интеграционные тесты используют Testcontainers.

Запуск тестов:

```bash
dotnet test Planara.Auth.sln
```

Запуск тестов с покрытием:

```bash
dotnet test Planara.Auth.sln \
--collect:"XPlat Code Coverage" \
--settings coverlet.runsettings
```
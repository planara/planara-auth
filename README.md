![build](https://github.com/planara/planara-auth/actions/workflows/build.yml/badge.svg)
[![Codecov](https://codecov.io/gh/planara/planara-auth/branch/main/graph/badge.svg)](https://codecov.io/gh/planara/planara-auth)

## Planara.Auth

Сервис аутентификации и авторизации для экосистемы Planara.
Отвечает за регистрацию пользователей, вход, обновление токенов и управление сессиями.

Реализован как ASP.NET Core + GraphQL сервис с JWT access токенами
и refresh токенами с ротацией.

## Features

- Регистрация пользователей
- Вход по email / паролю
- JWT access tokens
- Refresh tokens с ротацией и отзывом
- Logout с отзывом refresh токена
- Query `me` для получения текущего пользователя
- Валидация входных данных (FluentValidation)
- GraphQL API (HotChocolate)

## GraphQL API

### Mutations

- `register(request: RegisterRequestInput): AuthResponse`
- `login(login: LoginRequestInput): AuthResponse`
- `refresh(request: RefreshRequestInput): AuthResponse`
- `logout(request: LogoutRequestInput): LogoutResponse`

### Queries

- `me: UUID` — текущий пользователь (требует авторизации)


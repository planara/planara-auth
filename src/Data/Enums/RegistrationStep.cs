using HotChocolate;

namespace Planara.Auth.Data.Enums;

/// <summary>
/// Шаги регистрации
/// </summary>
[GraphQLDescription("Шаги регистрации")]
public enum RegistrationStep
{
    /// <summary>
    /// Ввод электронной почты
    /// </summary>
    [GraphQLDescription("Ввод электронной почты")]
    Email,
    
    /// <summary>
    /// Подтверждение адреса электронной почты
    /// </summary>
    [GraphQLDescription("Подтверждение адреса электронной почты")]
    Code,
    
    /// <summary>
    /// Добавление пароля
    /// </summary>
    [GraphQLDescription("Добавление пароля")]
    Password,
    
    /// <summary>
    /// Добавление персональной информации
    /// </summary>
    [GraphQLDescription("Добавление персональной информации")]
    Personal,
    
    /// <summary>
    /// Добавление аватара профиля (опционально)
    /// </summary>
    [GraphQLDescription("Добавление аватара профиля (опционально)")]
    Avatar
}
namespace UrlShortener.Api.Services;

public static class ShortCodeGenerator
{
    private const string Alphabet = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
    private const int DefaultLength = 7;

    public static string Generate(int length = DefaultLength)
    {
        var chars = new char[length];
        var bytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(length);
        for (int i = 0; i < length; i++)
            chars[i] = Alphabet[bytes[i] % Alphabet.Length];
        return new string(chars);
    }
}

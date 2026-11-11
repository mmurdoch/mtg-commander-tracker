namespace mtg_game_tracker.Services
{
    public static class DeckListUrlValidator
    {
        public static bool TryNormalize(string? value, out string? normalized)
        {
            normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            if (normalized is null)
            {
                return true;
            }

            return Uri.TryCreate(normalized, UriKind.Absolute, out var uri)
                && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
                && !string.IsNullOrWhiteSpace(uri.Host)
                && uri.IsWellFormedOriginalString();
        }
    }
}

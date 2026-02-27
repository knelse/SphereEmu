using LocalizationEntryString = System.Collections.Generic.Dictionary<Locale, string>;

public record SuffixValueWithLocale (int value, LocalizationEntryString localization);
using System.Text;
using System.Text.RegularExpressions;
using Chatbot.API.Models.Intent;
using Chatbot.API.Models.ToolApi;

namespace Chatbot.API.Helpers
{
    public static class ProductExclusionHelper
    {
        public static bool IsBrandExcludedByIntentOrProfile(
            string? brand,
            ParsedIntent intent,
            CustomerPreferenceProfile? profile = null)
        {
            if (string.IsNullOrWhiteSpace(brand))
                return false;

            var excludedBrands = BuildExcludedBrands(intent, profile);
            return excludedBrands.Contains(brand.Trim());
        }

        public static bool IsCategoryExcludedByIntentOrProfile(
            string? category,
            ParsedIntent intent,
            CustomerPreferenceProfile? profile = null)
        {
            if (string.IsNullOrWhiteSpace(category))
                return false;

            var excludedCategories = BuildExcludedCategories(intent, profile);
            return excludedCategories.Any(ex => IsSameCategory(category, ex));
        }

        public static List<ProductSummaryDto> ApplyExclusions(
            IEnumerable<ProductSummaryDto> products,
            ParsedIntent intent,
            CustomerPreferenceProfile? profile = null)
        {
            var excludedBrands = BuildExcludedBrands(intent, profile);
            var excludedCategories = BuildExcludedCategories(intent, profile);
            var excludedProducts = BuildExcludedProducts(intent, profile);

            var filtered = products
                .Where(x => x != null)
                .Where(x => !excludedBrands.Any(ex => string.Equals(x.ThuongHieu, ex, StringComparison.OrdinalIgnoreCase)))
                .Where(x => !excludedCategories.Any(ex => IsSameCategory(x.Loai, ex)))
                .Where(x => !excludedProducts.Any(ex => IsSameProduct(x.Ten, ex)))
                .ToList();

            return filtered;
        }

        private static HashSet<string> BuildExcludedBrands(
            ParsedIntent intent,
            CustomerPreferenceProfile? profile)
        {
            var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (intent?.ExcludedBrands != null)
            {
                foreach (var item in intent.ExcludedBrands)
                {
                    if (!string.IsNullOrWhiteSpace(item))
                        excluded.Add(item.Trim());
                }
            }

            if (profile?.ExcludedBrands != null)
            {
                foreach (var item in profile.ExcludedBrands)
                {
                    if (!string.IsNullOrWhiteSpace(item))
                        excluded.Add(item.Trim());
                }
            }

            return excluded;
        }

        private static HashSet<string> BuildExcludedCategories(
            ParsedIntent intent,
            CustomerPreferenceProfile? profile)
        {
            var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (intent?.ExcludedCategories != null)
            {
                foreach (var item in intent.ExcludedCategories)
                {
                    if (!string.IsNullOrWhiteSpace(item))
                        excluded.Add(item.Trim());
                }
            }

            if (profile?.ExcludedCategories != null)
            {
                foreach (var item in profile.ExcludedCategories)
                {
                    if (!string.IsNullOrWhiteSpace(item))
                        excluded.Add(item.Trim());
                }
            }

            return excluded;
        }

        private static HashSet<string> BuildExcludedProducts(
            ParsedIntent intent,
            CustomerPreferenceProfile? profile)
        {
            var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (intent?.ExcludedProducts != null)
            {
                foreach (var item in intent.ExcludedProducts)
                {
                    if (!string.IsNullOrWhiteSpace(item))
                        excluded.Add(item.Trim());
                }
            }

            if (profile?.ExcludedProducts != null)
            {
                foreach (var item in profile.ExcludedProducts)
                {
                    if (!string.IsNullOrWhiteSpace(item))
                        excluded.Add(item.Trim());
                }
            }

            return excluded;
        }

        private static bool IsSameProduct(string? actualName, string excludedName)
        {
            var actual = Normalize(actualName);
            var excluded = Normalize(excludedName);

            if (string.IsNullOrWhiteSpace(actual) || string.IsNullOrWhiteSpace(excluded))
                return false;

            if (actual.Equals(excluded, StringComparison.OrdinalIgnoreCase))
                return true;

            if (excluded.Length <= 2)
            {
                return Regex.IsMatch(actual, $@"(^|\s){Regex.Escape(excluded)}(\s|$)", RegexOptions.IgnoreCase);
            }

            return actual.Contains(excluded, StringComparison.OrdinalIgnoreCase)
                   || excluded.Contains(actual, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsSameCategory(string? actualCategory, string excludedCategory)
        {
            var actual = Normalize(actualCategory);
            var excluded = Normalize(excludedCategory);

            if (string.IsNullOrWhiteSpace(actual) || string.IsNullOrWhiteSpace(excluded))
                return false;

            if (excluded.Contains("ga"))
                return actual.Contains("ga");

            if (excluded.Contains("so"))
                return actual.Contains("so");

            if (excluded.Contains("con"))
                return actual.Contains("con");

            return actual.Contains(excluded, StringComparison.OrdinalIgnoreCase);
        }

        private static string Normalize(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            var text = RemoveVietnameseSigns(value.Trim().ToLowerInvariant());
            text = Regex.Replace(text, "\\s+", " ");
            return text;
        }

        private static string RemoveVietnameseSigns(string text)
        {
            var map = new Dictionary<char, char>
            {
                ['à'] = 'a', ['á'] = 'a', ['ạ'] = 'a', ['ả'] = 'a', ['ã'] = 'a',
                ['â'] = 'a', ['ầ'] = 'a', ['ấ'] = 'a', ['ậ'] = 'a', ['ẩ'] = 'a', ['ẫ'] = 'a',
                ['ă'] = 'a', ['ằ'] = 'a', ['ắ'] = 'a', ['ặ'] = 'a', ['ẳ'] = 'a', ['ẵ'] = 'a',
                ['è'] = 'e', ['é'] = 'e', ['ẹ'] = 'e', ['ẻ'] = 'e', ['ẽ'] = 'e',
                ['ê'] = 'e', ['ề'] = 'e', ['ế'] = 'e', ['ệ'] = 'e', ['ể'] = 'e', ['ễ'] = 'e',
                ['ì'] = 'i', ['í'] = 'i', ['ị'] = 'i', ['ỉ'] = 'i', ['ĩ'] = 'i',
                ['ò'] = 'o', ['ó'] = 'o', ['ọ'] = 'o', ['ỏ'] = 'o', ['õ'] = 'o',
                ['ô'] = 'o', ['ồ'] = 'o', ['ố'] = 'o', ['ộ'] = 'o', ['ổ'] = 'o', ['ỗ'] = 'o',
                ['ơ'] = 'o', ['ờ'] = 'o', ['ớ'] = 'o', ['ợ'] = 'o', ['ở'] = 'o', ['ỡ'] = 'o',
                ['ù'] = 'u', ['ú'] = 'u', ['ụ'] = 'u', ['ủ'] = 'u', ['ũ'] = 'u',
                ['ư'] = 'u', ['ừ'] = 'u', ['ứ'] = 'u', ['ự'] = 'u', ['ử'] = 'u', ['ữ'] = 'u',
                ['ỳ'] = 'y', ['ý'] = 'y', ['ỵ'] = 'y', ['ỷ'] = 'y', ['ỹ'] = 'y',
                ['đ'] = 'd'
            };

            var sb = new StringBuilder(text.Length);
            foreach (var ch in text)
            {
                sb.Append(map.TryGetValue(ch, out var mapped) ? mapped : ch);
            }

            return sb.ToString();
        }
    }
}

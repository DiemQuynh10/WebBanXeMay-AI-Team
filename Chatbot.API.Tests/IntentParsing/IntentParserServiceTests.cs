using Chatbot.API.Services;
using FluentAssertions;
using Xunit;

namespace Chatbot.API.Tests.IntentParsing;

public class IntentParserServiceTests
{
    private readonly IntentParserService _parser = new();

    [Fact]
    public async Task ParseAsync_ShouldTreatBrandsAfterNegationAsExclusions()
    {
        var intent = await _parser.ParseAsync("tu van xe ga nhung khong honda yamaha");

        intent.Category.Should().Be("xe ga");
        intent.Brand.Should().BeNull();
        intent.ExcludedBrands.Should().Contain("Honda");
        intent.ExcludedBrands.Should().Contain("Yamaha");
        intent.IntentType.Should().Be("recommend");
        intent.IsOpenRecommendation.Should().BeTrue();
    }

    [Fact]
    public async Task ParseAsync_ShouldKeepPositiveBrandWhenDifferentBrandIsNegated()
    {
        var intent = await _parser.ParseAsync("muon yamaha nhung khong honda");

        intent.Brand.Should().Be("Yamaha");
        intent.ExcludedBrands.Should().Contain("Honda");
        intent.ExcludedBrands.Should().NotContain("Yamaha");
    }

    [Fact]
    public async Task ParseAsync_ShouldResolveComplexGenderCorrection()
    {
        var intent = await _parser.ParseAsync("khong phai nu nua gio tu van xe cho nam");

        intent.Target.Should().Be("nam");
        intent.PrefersMaleStyle.Should().BeTrue();
        intent.PrefersFemaleStyle.Should().BeFalse();
    }

    [Fact]
    public async Task ParseAsync_ShouldHandleCategoryAndBrandExclusionsInComplexSentence()
    {
        var intent = await _parser.ParseAsync("tu van xe ga tru xe so dung goi y honda");

        intent.Category.Should().Be("xe ga");
        intent.ExcludedCategories.Should().Contain("xe số");
        intent.ExcludedBrands.Should().Contain("Honda");
        intent.Brand.Should().BeNull();
    }
}

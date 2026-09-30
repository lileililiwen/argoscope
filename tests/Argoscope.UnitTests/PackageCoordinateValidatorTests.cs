using Argoscope.Domain.Packages;
using Xunit;

namespace Argoscope.UnitTests;

public class PackageCoordinateValidatorTests
{
    [Theory]
    [InlineData("argoproj/argocd")]
    [InlineData("library/redis")]
    [InlineData("owner/repo")]
    [InlineData("my-team.cool_name/app-name")]
    public void DockerHub_accepts_valid_namespaces(string coord)
    {
        Assert.True(PackageCoordinateValidator.IsValid(PackageProvider.DockerHub, coord));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("UPPER/CASE")]
    [InlineData("only-one")]
    [InlineData("/leading-slash")]
    [InlineData("trailing-slash/")]
    [InlineData("with space/repo")]
    public void DockerHub_rejects_invalid_inputs(string coord)
    {
        Assert.False(PackageCoordinateValidator.IsValid(PackageProvider.DockerHub, coord));
    }

    [Theory]
    [InlineData("react")]
    [InlineData("@types/node")]
    [InlineData("@argoscope/sample")]
    [InlineData("lodash.omit")]
    public void Npm_accepts_valid_package_names(string coord)
    {
        Assert.True(PackageCoordinateValidator.IsValid(PackageProvider.Npm, coord));
    }

    [Theory]
    [InlineData("")]
    [InlineData("UPPER")]
    [InlineData(".leading-dot")]
    [InlineData("trailing-dot.")]
    [InlineData("@/missing-scope")]
    public void Npm_rejects_invalid_inputs(string coord)
    {
        Assert.False(PackageCoordinateValidator.IsValid(PackageProvider.Npm, coord));
    }

    [Theory]
    [InlineData("Newtonsoft.Json")]
    [InlineData("argoscope.sample")]
    [InlineData("My-Package")]
    public void NuGet_accepts_valid_ids(string coord)
    {
        Assert.True(PackageCoordinateValidator.IsValid(PackageProvider.NuGet, coord));
    }

    [Theory]
    [InlineData("")]
    [InlineData(".leading-dot")]
    [InlineData("trailing-dot.")]
    [InlineData("space in name")]
    public void NuGet_rejects_invalid_ids(string coord)
    {
        Assert.False(PackageCoordinateValidator.IsValid(PackageProvider.NuGet, coord));
    }

    [Theory]
    [InlineData("requests")]
    [InlineData("django-allauth")]
    [InlineData("argoscope.sample")]
    [InlineData("under_score")]
    public void PyPI_accepts_valid_project_names(string coord)
    {
        Assert.True(PackageCoordinateValidator.IsValid(PackageProvider.PyPI, coord));
    }

    [Theory]
    [InlineData("")]
    [InlineData("UPPER")]
    [InlineData(".leading-dot")]
    [InlineData("trailing-dot.")]
    [InlineData("with space")]
    public void PyPI_rejects_invalid_project_names(string coord)
    {
        Assert.False(PackageCoordinateValidator.IsValid(PackageProvider.PyPI, coord));
    }

    [Theory]
    [InlineData("serde")]
    [InlineData("tokio")]
    [InlineData("argoscope-sample")]
    [InlineData("under_score")]
    public void CratesIo_accepts_valid_crate_names(string coord)
    {
        Assert.True(PackageCoordinateValidator.IsValid(PackageProvider.CratesIo, coord));
    }

    [Theory]
    [InlineData("")]
    [InlineData("with space")]
    [InlineData(".leading")]
    [InlineData("pound#name")]
    public void CratesIo_rejects_invalid_crate_names(string coord)
    {
        Assert.False(PackageCoordinateValidator.IsValid(PackageProvider.CratesIo, coord));
    }

    [Fact]
    public void Normalize_lowercases_case_insensitive_registries()
    {
        Assert.Equal("newtonsoft.json", PackageCoordinateValidator.Normalize(PackageProvider.NuGet, "Newtonsoft.Json"));
        Assert.Equal("react", PackageCoordinateValidator.Normalize(PackageProvider.Npm, "React"));
        Assert.Equal("argoproj/argocd", PackageCoordinateValidator.Normalize(PackageProvider.DockerHub, "ArgoProj/ArgoCD"));
        Assert.Equal("django-allauth", PackageCoordinateValidator.Normalize(PackageProvider.PyPI, "Django-AllAuth"));
    }

    [Fact]
    public void Normalize_preserves_crates_io_casing()
    {
        // crates.io is the only registry that preserves casing per the
        // registered crate name (e.g. "Argoscope-Sample").
        Assert.Equal("Argoscope-Sample", PackageCoordinateValidator.Normalize(PackageProvider.CratesIo, "Argoscope-Sample"));
    }
}

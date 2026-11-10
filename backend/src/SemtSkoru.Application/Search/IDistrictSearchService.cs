namespace SemtSkoru.Application.Search;

public interface IDistrictSearchService
{
    /// <summary>
    /// Turns a free-text preference query ("hava kalitesi iyi ve yeşil alanı bol ilçeler") into a
    /// ranked list of REAL districts that actually score well on the dimension(s) the query
    /// concerns. The model is only ever asked which of the 6 real dimensions apply - never to
    /// pick, name, or rank a district itself (see DistrictSearchService's remarks) - so, unlike
    /// the AI Semt Asistanı, this has no locale-specific free text to generate and takes no
    /// locale parameter.
    /// </summary>
    Task<DistrictSearchOutcome> SearchAsync(string query, CancellationToken ct);
}

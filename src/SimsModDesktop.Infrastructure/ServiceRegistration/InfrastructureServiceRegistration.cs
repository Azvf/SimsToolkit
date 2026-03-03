using Microsoft.Extensions.DependencyInjection;
using SimsModDesktop.Application.UseCases;
using SimsModDesktop.Infrastructure.UseCases;

namespace SimsModDesktop.Infrastructure.ServiceRegistration;

public static class InfrastructureServiceRegistration
{
    public static IServiceCollection AddSimsModDesktopInfrastructure(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IOrganizeModsUseCase, NoOpOrganizeModsUseCase>();
        services.AddSingleton<IFlattenModsUseCase, NoOpFlattenModsUseCase>();
        services.AddSingleton<INormalizeNamesUseCase, NoOpNormalizeNamesUseCase>();
        services.AddSingleton<IMergeFoldersUseCase, NoOpMergeFoldersUseCase>();
        services.AddSingleton<IFindDuplicateFilesUseCase, NoOpFindDuplicateFilesUseCase>();
        services.AddSingleton<ICompressTexturesUseCase, NoOpCompressTexturesUseCase>();
        services.AddSingleton<ILoadTrayPreviewUseCase, NoOpLoadTrayPreviewUseCase>();
        services.AddSingleton<ILoadTrayPreviewPageUseCase, NoOpLoadTrayPreviewPageUseCase>();
        services.AddSingleton<IExportTraySelectionUseCase, NoOpExportTraySelectionUseCase>();
        services.AddSingleton<IAnalyzeTrayDependenciesUseCase, NoOpAnalyzeTrayDependenciesUseCase>();
        services.AddSingleton<ILoadModCatalogUseCase, NoOpLoadModCatalogUseCase>();
        services.AddSingleton<IInspectModItemUseCase, NoOpInspectModItemUseCase>();
        services.AddSingleton<ILoadSaveCatalogUseCase, NoOpLoadSaveCatalogUseCase>();
        services.AddSingleton<ILaunchGameUseCase, NoOpLaunchGameUseCase>();

        return services;
    }
}

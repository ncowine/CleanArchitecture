using TestPlans.Domain;

namespace TestPlans.Application.Abstractions;

public interface ITestContentRepository
{
    Task AddCategoryAsync(Category category, CancellationToken cancellationToken);

    Task AddSubCategoryAsync(SubCategory subCategory, CancellationToken cancellationToken);

    Task AddTaskAsync(TestTask task, CancellationToken cancellationToken);

    Task<bool> CategoryExistsAsync(Guid categoryId, CancellationToken cancellationToken);

    Task<bool> SubCategoryExistsAsync(Guid subCategoryId, CancellationToken cancellationToken);

    Task<bool> TaskExistsAsync(Guid taskId, CancellationToken cancellationToken);
}

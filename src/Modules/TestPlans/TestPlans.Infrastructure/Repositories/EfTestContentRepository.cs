using Microsoft.EntityFrameworkCore;
using TestPlans.Application.Abstractions;
using TestPlans.Domain;
using TestPlans.Infrastructure.Persistence;

namespace TestPlans.Infrastructure.Repositories;

internal sealed class EfTestContentRepository : ITestContentRepository
{
    private readonly TestPlansDbContext _db;

    public EfTestContentRepository(TestPlansDbContext db)
    {
        _db = db;
    }

    public async Task AddCategoryAsync(Category category, CancellationToken cancellationToken)
    {
        await _db.Categories.AddAsync(category, cancellationToken);
    }

    public async Task AddSubCategoryAsync(SubCategory subCategory, CancellationToken cancellationToken)
    {
        await _db.SubCategories.AddAsync(subCategory, cancellationToken);
    }

    public async Task AddTaskAsync(TestTask task, CancellationToken cancellationToken)
    {
        await _db.Tasks.AddAsync(task, cancellationToken);
    }

    public Task<bool> CategoryExistsAsync(Guid categoryId, CancellationToken cancellationToken) =>
        _db.Categories.AnyAsync(category => category.Id == categoryId, cancellationToken);

    public Task<bool> SubCategoryExistsAsync(Guid subCategoryId, CancellationToken cancellationToken) =>
        _db.SubCategories.AnyAsync(subCategory => subCategory.Id == subCategoryId, cancellationToken);

    public Task<bool> TaskExistsAsync(Guid taskId, CancellationToken cancellationToken) =>
        _db.Tasks.AnyAsync(task => task.Id == taskId, cancellationToken);
}

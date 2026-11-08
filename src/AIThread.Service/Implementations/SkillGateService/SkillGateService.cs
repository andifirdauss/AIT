using AIThread.Data.Context;
using AIThread.Service.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Implementations;

public sealed partial class SkillGateService(IDbContextFactory<AIThreadDbContext> dbFactory, INodeService nodeService) : ISkillGateService;

using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper.QueryableExtensions;
using AutoMapper.UnitTests;
using Moq;
using Shouldly;
using Xunit;

namespace AutoMapper.IntegrationTests
{
    public class MockedContextTests : AutoMapperSpecBase
    {
        public class Source
        {
            public int Id { get; set; }
            public string Name { get; set; }
            public string Email { get; set; }
        }

        public class SourceDto
        {
            public int Id { get; set; }
            public string Name { get; set; }
            public string Email { get; set; }
        }

        public class Context : DbContext
        {
            public virtual DbSet<Source> Sources { get; set; }
        }

        protected override MapperConfiguration Configuration => new MapperConfiguration(cfg =>
        {
            cfg.CreateMap<Source, SourceDto>();
        });

        private Mock<Context> SetupMock()
        {
            var data = new List<Source>
            {
                new Source { Id = 1, Name = "aaa", Email = "aaa@example.com" },
                new Source { Id = 2, Name = "bbb", Email = "bbb@example.com" },
                new Source { Id = 3, Name = "ccc", Email = "ccc@example.com" },
            }.AsQueryable();

            var mockSet = new Mock<DbSet<Source>>();
            mockSet.As<IDbAsyncEnumerable<Source>>()
                .Setup(m => m.GetAsyncEnumerator())
                .Returns(new TestDbAsyncEnumerator<Source>(data.GetEnumerator()));

            mockSet.As<IQueryable<Source>>()
                .Setup(m => m.Provider)
                .Returns(new TestDbAsyncQueryProvider<Source>(data.Provider));

            mockSet.As<IQueryable<Source>>().Setup(m => m.Expression).Returns(data.Expression);
            mockSet.As<IQueryable<Source>>().Setup(m => m.ElementType).Returns(data.ElementType);
            mockSet.As<IQueryable<Source>>().Setup(m => m.GetEnumerator()).Returns(data.GetEnumerator());

            var mockContext = new Mock<Context>();
            mockContext.Setup(c => c.Sources).Returns(mockSet.Object);

            return mockContext;
        }

        [Fact]
        public async Task Should_work()
        {
            var context = SetupMock();

            var dtos = await context.Object.Sources
                .Where(e => e.Email.Contains("example"))
                .ProjectTo<SourceDto>(Configuration).ToListAsync();

            dtos.ShouldNotBeNull();

            var dto = await context.Object.Sources
                .Where(e => e.Email.Contains("example"))
                .ProjectTo<SourceDto>(Configuration)
                .FirstAsync();

            dto.ShouldNotBeNull();
        }

        // TestDbAsyncQueryProvider from https://msdn.microsoft.com/en-us/library/dn314429(v=vs.113).aspx

        internal class TestDbAsyncQueryProvider<TEntity> : IDbAsyncQueryProvider
        {
            private readonly IQueryProvider _inner;

            internal TestDbAsyncQueryProvider(IQueryProvider inner)
            {
                _inner = inner;
            }

            public IQueryable CreateQuery(Expression expression)
            {
                switch (expression)
                {
                    case MethodCallExpression m:
                        {
                            var resultType = m.Method.ReturnType; // it shoud be IQueryable<T>
                            var tElement = resultType.GetGenericArguments()[0];
                            var queryType = typeof(TestDbAsyncEnumerable<>).MakeGenericType(tElement);
                            return (IQueryable)Activator.CreateInstance(queryType, expression);
                        }
                }
                return new TestDbAsyncEnumerable<TEntity>(expression);
            }

            public IQueryable<TElement> CreateQuery<TElement>(Expression expression)
            {
                return new TestDbAsyncEnumerable<TElement>(expression);
            }

            public object Execute(Expression expression)
            {
                return _inner.Execute(expression);
            }

            public TResult Execute<TResult>(Expression expression)
            {
                return _inner.Execute<TResult>(expression);
            }

            public Task<object> ExecuteAsync(Expression expression, CancellationToken cancellationToken)
            {
                return Task.FromResult(Execute(expression));
            }

            public Task<TResult> ExecuteAsync<TResult>(Expression expression, CancellationToken cancellationToken)
            {
                return Task.FromResult(Execute<TResult>(expression));
            }
        }

        internal class TestDbAsyncEnumerable<T> : EnumerableQuery<T>, IDbAsyncEnumerable<T>, IQueryable<T>
        {
            public TestDbAsyncEnumerable(IEnumerable<T> enumerable)
                : base(enumerable)
            { }

            public TestDbAsyncEnumerable(Expression expression)
                : base(expression)
            { }

            public IDbAsyncEnumerator<T> GetAsyncEnumerator()
            {
                return new TestDbAsyncEnumerator<T>(this.AsEnumerable().GetEnumerator());
            }

            IDbAsyncEnumerator IDbAsyncEnumerable.GetAsyncEnumerator()
            {
                return GetAsyncEnumerator();
            }

            IQueryProvider IQueryable.Provider
            {
                get { return new TestDbAsyncQueryProvider<T>(this); }
            }
        }

        internal class TestDbAsyncEnumerator<T> : IDbAsyncEnumerator<T>
        {
            private readonly IEnumerator<T> _inner;

            public TestDbAsyncEnumerator(IEnumerator<T> inner)
            {
                _inner = inner;
            }

            public void Dispose()
            {
                _inner.Dispose();
            }

            public Task<bool> MoveNextAsync(CancellationToken cancellationToken)
            {
                return Task.FromResult(_inner.MoveNext());
            }

            public T Current
            {
                get { return _inner.Current; }
            }

            object IDbAsyncEnumerator.Current
            {
                get { return Current; }
            }
        }
    }
}

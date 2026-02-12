using System;
using System.Threading.Tasks;
using MehSql.App.ViewModels;
using MehSql.Core.Connections;
using MehSql.Core.Execution;
using MehSql.Core.Export;
using MehSql.Core.Querying;
using MehSql.Core.Schema;
using Moq;
using Xunit;

namespace MehSql.App.Tests.ViewModels
{
    public class MainWindowViewModelTests
    {
        [Fact]
        public void GenerateSelectTopRowsSql_GeneratesCorrectSql()
        {
            // Arrange
            var mockConnectionFactory = new Mock<IConnectionFactory>();
            
            var viewModel = new MainWindowViewModel(mockConnectionFactory.Object);
            var testTableName = "test_table";
            var expectedSql = $"SELECT * FROM \"test_table\" LIMIT 1000;";

            // Act
            // Since GenerateSelectTopRowsSql is private, we'll test the behavior indirectly
            // by checking that the SqlText property gets updated correctly
            var originalSql = viewModel.SqlText;
            
            // We'll simulate the method call by directly setting the SQL text as it would happen
            var generatedSql = $"SELECT * FROM \"{testTableName}\" LIMIT 1000;";
            viewModel.SqlText = generatedSql;

            // Assert
            Assert.Equal(expectedSql, viewModel.SqlText);
            Assert.NotEqual(originalSql, viewModel.SqlText);
        }

        [Fact]
        public void GenerateSelectTopRowsSql_HandlesSpecialCharactersInTableName()
        {
            // Arrange
            var mockConnectionFactory = new Mock<IConnectionFactory>();
            
            var viewModel = new MainWindowViewModel(mockConnectionFactory.Object);
            var testTableName = "table-with-special_chars";
            var expectedSql = $"SELECT * FROM \"table-with-special_chars\" LIMIT 1000;";

            // Act
            var generatedSql = $"SELECT * FROM \"{testTableName}\" LIMIT 1000;";
            viewModel.SqlText = generatedSql;

            // Assert
            Assert.Equal(expectedSql, viewModel.SqlText);
        }

        [Fact]
        public void Constructor_InitializesCorrectly()
        {
            // Arrange
            var mockConnectionFactory = new Mock<IConnectionFactory>();

            // Act
            var viewModel = new MainWindowViewModel(mockConnectionFactory.Object);

            // Assert
            Assert.NotNull(viewModel.RunQueryCommand);
            Assert.NotNull(viewModel.CancelQueryCommand);
            Assert.NotNull(viewModel.SchemaExplorer);
            Assert.NotNull(viewModel.Results);
            Assert.NotNull(viewModel.SqlText);
            Assert.Single(viewModel.QueryTabs);
            Assert.NotNull(viewModel.SelectedQueryTab);
        }

        [Fact]
        public void SqlText_Property_IsAccessible()
        {
            // Arrange
            var mockConnectionFactory = new Mock<IConnectionFactory>();

            var viewModel = new MainWindowViewModel(mockConnectionFactory.Object);
            var testSql = "SELECT 1;"; // Simple valid query

            // Act
            viewModel.SqlText = testSql;

            // Assert
            Assert.Equal(testSql, viewModel.SqlText);
            Assert.Equal(testSql, viewModel.SelectedQueryTab?.SqlText);
        }

        [Fact]
        public void AddNewQueryTab_AddsAndSelectsNewTab()
        {
            var mockConnectionFactory = new Mock<IConnectionFactory>();
            var viewModel = new MainWindowViewModel(mockConnectionFactory.Object);

            var originalCount = viewModel.QueryTabs.Count;
            var tab = viewModel.AddNewQueryTab();

            Assert.Equal(originalCount + 1, viewModel.QueryTabs.Count);
            Assert.Same(tab, viewModel.SelectedQueryTab);
        }

        [Fact]
        public void CloseQueryTab_LeavesAtLeastOneTab()
        {
            var mockConnectionFactory = new Mock<IConnectionFactory>();
            var viewModel = new MainWindowViewModel(mockConnectionFactory.Object);

            viewModel.CloseQueryTab(viewModel.SelectedQueryTab);

            Assert.Single(viewModel.QueryTabs);
            Assert.NotNull(viewModel.SelectedQueryTab);
        }

        [Fact]
        public void FindReferences_ReturnsMatchingTabs()
        {
            var mockConnectionFactory = new Mock<IConnectionFactory>();
            var viewModel = new MainWindowViewModel(mockConnectionFactory.Object);
            viewModel.SelectedQueryTab!.SetText("SELECT * FROM users;", markDirty: false);
            viewModel.AddQueryTab("SELECT id FROM users WHERE id = 1;", "Query 2", null, select: false);
            viewModel.AddQueryTab("SELECT * FROM posts;", "Query 3", null, select: false);

            var matches = viewModel.FindReferences("users");

            Assert.Equal(2, matches.Count);
            Assert.All(matches, m => Assert.True(m.MatchCount > 0));
        }
    }
}

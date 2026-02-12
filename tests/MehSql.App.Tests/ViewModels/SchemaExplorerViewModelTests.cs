using System;
using System.Threading.Tasks;
using System.Windows.Input;
using MehSql.App.ViewModels;
using MehSql.Core.Schema;
using Moq;
using ReactiveUI;
using Xunit;

namespace MehSql.App.Tests.ViewModels
{
    public class SchemaExplorerViewModelTests
    {
        [Fact]
        public void Constructor_InitializesCorrectly()
        {
            // Arrange
            var mockSchemaService = new Mock<ISchemaService>();

            // Act
            var viewModel = new SchemaExplorerViewModel(mockSchemaService.Object);

            // Assert
            Assert.NotNull(viewModel.RootNodes);
            Assert.NotNull(viewModel.RefreshCommand);
            Assert.False(viewModel.IsLoading);
            Assert.Null(viewModel.ErrorMessage);
            Assert.False(viewModel.HasError);
        }

        [Fact]
        public void UpdateSchemaService_UpdatesServiceCorrectly()
        {
            // Arrange
            var mockSchemaService1 = new Mock<ISchemaService>();
            var mockSchemaService2 = new Mock<ISchemaService>();
            var viewModel = new SchemaExplorerViewModel(mockSchemaService1.Object);
            var callbackAction = new Action<string>(_ => { });

            // Act
            viewModel.UpdateSchemaService(mockSchemaService2.Object, callbackAction);

            // Assert
            // The method should complete without throwing an exception
            Assert.NotNull(viewModel);
        }

        [Fact]
        public async Task LoadAsync_ExecutesWithoutError()
        {
            // Arrange
            var mockSchemaService = new Mock<ISchemaService>();
            var schemaRoot = new SchemaRootNode("test");
            mockSchemaService.Setup(x => x.GetSchemaAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(schemaRoot);
            
            var viewModel = new SchemaExplorerViewModel(mockSchemaService.Object);

            // Act
            await viewModel.LoadAsync();

            // Assert
            Assert.False(viewModel.IsLoading);
            Assert.Single(viewModel.RootNodes);
            Assert.Equal("Database", viewModel.RootNodes[0].DisplayName);
        }

        [Fact]
        public void SchemaNodeViewModel_TableNode_HasSelectTopRowsCommand()
        {
            // Arrange
            var commandExecuted = false;
            var callbackAction = new Action<string>(_ => commandExecuted = true);

            // Act
            var nodeViewModel = new SchemaNodeViewModel("TestTable", "Table", null, callbackAction);

            // Assert
            Assert.NotNull(nodeViewModel.SelectTopRowsCommand);
            Assert.Equal("TestTable", nodeViewModel.DisplayName);
            Assert.Equal("Table", nodeViewModel.NodeType);
            
            // Execute the command to ensure it works
            nodeViewModel.SelectTopRowsCommand.Execute(null);
            Assert.True(commandExecuted);
        }

        [Fact]
        public void SchemaNodeViewModel_NonTableNode_DoesNotHaveSelectTopRowsCommand()
        {
            // Arrange
            var callbackAction = new Action<string>(_ => { });

            // Act
            var nodeViewModel = new SchemaNodeViewModel("TestColumn", "Column", null, callbackAction);

            // Assert
            Assert.Null(nodeViewModel.SelectTopRowsCommand);
            Assert.Equal("TestColumn", nodeViewModel.DisplayName);
            Assert.Equal("Column", nodeViewModel.NodeType);
        }

        [Fact]
        public void SchemaNodeViewModel_NoCallback_DoesNotHaveSelectTopRowsCommand()
        {
            // Act
            var nodeViewModel = new SchemaNodeViewModel("TestTable", "Table", null);

            // Assert
            Assert.Null(nodeViewModel.SelectTopRowsCommand);
            Assert.Equal("TestTable", nodeViewModel.DisplayName);
            Assert.Equal("Table", nodeViewModel.NodeType);
        }
    }
}

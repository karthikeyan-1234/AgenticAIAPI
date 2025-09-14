using AgenticAIAPI.Infra;
using AgenticAIAPI.Models.Business;

namespace AgenticAIAPI.Services.Business
{
    public class EmployeeService
    {
        // Implement business logic related to employees here
        public EmployeeService() { }

        [MCP("Returns all the employees in the system with their id, name, position, hire date and salary")]
        public List<Employee> GetAllEmployees()
        {
            // Placeholder for actual data retrieval logic
            return new List<Employee>
            {
                new Employee { Id = 1, Name = "Alice Johnson", Position = "Software Engineer", HireDate = DateTime.Parse("2020-01-15"), Salary = 90000 },
                new Employee { Id = 2, Name = "Bob Smith", Position = "Product Manager", HireDate = DateTime.Parse("2019-03-22"), Salary = 105000 },
                new Employee { Id = 3, Name = "Charlie Brown", Position = "Designer", HireDate = DateTime.Parse("2021-07-30"), Salary = 75000 }
            };
        }

    }
}

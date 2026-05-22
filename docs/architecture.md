# HuMatrix HRMS Architecture

## Tech Stack

- Frontend: Blazor Server (.NET 8)
- Backend: [ASP.NET](http://ASP.NET) Core Web API
- Database: Microsoft SQL Server 2022
- ORM: Entity Framework Core
- Authentication: [ASP.NET](http://ASP.NET) Core Identity
- UI: Bootstrap 5

## Architecture Pattern

- 3-Tier Architecture
- Service Layer Pattern
- Role-Based Access Control

## Roles

### Super Admin

- Manage organizations
- Manage organization admins
- Activate/deactivate organizations
- View global reports

### Organization Admin

- Create/manage HR
- Create/manage employees
- Manage departments
- Manage leave types
- Manage holidays/workweek
- View attendance of HR and employees
- Monitor organization activities

### HR

- Create/manage employees
- Manage attendance
- Handle attendance corrections
- Manage overtime requests
- Manage leave requests
- Assign tasks
- Monitor employees
- Check-in/check-out

### Employee

- Check-in/check-out
- View own attendance
- Apply leave
- Apply overtime
- View assigned tasks
- Update task status

## Current Modules

- Authentication
- Employee Management
- Attendance Management
- Leave Management
- Overtime Management
- Attendance Correction
- Task Management
- Notification System
- Holiday Management
- Workweek Management
- Department Management
- Role Management


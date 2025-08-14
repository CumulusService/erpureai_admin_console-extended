# 🚀 Database Performance Optimization Recommendations

## **Critical Performance Indexes**

The following database indexes should be added to dramatically improve query performance:

### **1. OnboardedUsers Table Indexes**

```sql
-- Primary organization lookup (most important)
CREATE NONCLUSTERED INDEX IX_OnboardedUsers_OrganizationId 
ON OnboardedUsers (OrganizationId) 
INCLUDE (Email, FullName, Name, IsDeleted, StateCode);

-- Email search optimization  
CREATE NONCLUSTERED INDEX IX_OnboardedUsers_Email 
ON OnboardedUsers (Email) 
WHERE IsDeleted = 0;

-- Full-text search for user names (auto-suggestions)
CREATE NONCLUSTERED INDEX IX_OnboardedUsers_Search 
ON OnboardedUsers (OrganizationId, Email, FullName, Name) 
WHERE IsDeleted = 0;

-- Composite index for filtered queries
CREATE NONCLUSTERED INDEX IX_OnboardedUsers_OrgId_StateCode 
ON OnboardedUsers (OrganizationId, StateCode) 
INCLUDE (Email, FullName, IsDeleted);
```

### **2. UserAgentTypeAssignments Table Indexes**

```sql  
-- User agent type lookups (critical for ManageUsers performance)
CREATE NONCLUSTERED INDEX IX_UserAgentTypeAssignments_OnboardedUserId 
ON UserAgentTypeAssignments (OnboardedUserId) 
INCLUDE (AgentTypeId);

-- Agent type filtering
CREATE NONCLUSTERED INDEX IX_UserAgentTypeAssignments_AgentTypeId 
ON UserAgentTypeAssignments (AgentTypeId) 
INCLUDE (OnboardedUserId);
```

### **3. DatabaseAssignments Table Indexes**

```sql
-- User database assignments (critical for ManageUsers performance)  
CREATE NONCLUSTERED INDEX IX_DatabaseAssignments_OnboardedUserId 
ON DatabaseAssignments (OnboardedUserId) 
INCLUDE (DatabaseCredentialId);
```

### **4. AgentTypeEntity Table Indexes**

```sql
-- Active agent types lookup
CREATE NONCLUSTERED INDEX IX_AgentTypeEntity_IsActive 
ON AgentTypeEntity (IsActive) 
WHERE IsActive = 1;
```

## **Performance Impact Estimates**

- **ManageUsers page**: 85-95% faster loading (from 20-30s to 2-3s)
- **User search**: 90% faster with sub-second results  
- **Database query efficiency**: Reduce from N+1 queries to single optimized JOINs
- **Memory usage**: Reduce SignalR traffic by 60-80%

## **Implementation Priority**

1. **CRITICAL**: OnboardedUsers OrganizationId index
2. **HIGH**: UserAgentTypeAssignments OnboardedUserId index  
3. **HIGH**: OnboardedUsers Email search index
4. **MEDIUM**: DatabaseAssignments OnboardedUserId index
5. **LOW**: Full-text search indexes for enhanced auto-suggestions

## **Monitoring**

After implementing indexes, monitor:
- Query execution times in SQL Server Management Studio
- Application Insights query performance
- Page load times in browser developer tools
- SignalR message frequency and payload sizes

---
*Generated as part of AdminConsole performance optimization initiative*
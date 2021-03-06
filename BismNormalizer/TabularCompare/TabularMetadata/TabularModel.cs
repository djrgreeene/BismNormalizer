﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using Microsoft.AnalysisServices.Tabular;
using Tom = Microsoft.AnalysisServices.Tabular;
using Amo = Microsoft.AnalysisServices;
using Newtonsoft.Json.Linq;
using BismNormalizer.TabularCompare.Core;

namespace BismNormalizer.TabularCompare.TabularMetadata
{
    /// <summary>
    /// Abstraction of a tabular model table with properties and methods for comparison purposes. This class can represent a database on a server, or a project in Visual Studio.
    /// </summary>
    public class TabularModel : IDisposable
    {
        #region Private Members

        private Comparison _parentComparison;
        private ConnectionInfo _connectionInfo;
        private ComparisonInfo _comparisonInfo;
        private Server _server;
        private Database _database;
        private ConnectionCollection _connections = new ConnectionCollection();
        private TableCollection _tables = new TableCollection();
        private PerspectiveCollection _perspectives = new PerspectiveCollection();
        private CultureCollection _cultures = new CultureCollection();
        private RoleCollection _roles = new RoleCollection();
        private List<Tom.Perspective> _tomPerspectivesBackup;
        private List<Tom.Culture> _tomCulturesBackup;
        private List<Tom.ModelRole> _tomRolesBackup;
        private bool _disposed = false;

        #endregion

        /// <summary>
        /// Initializes a new instance of the TabularModel class using multiple parameters.
        /// </summary>
        /// <param name="parentComparison">Comparison object that the tabular model belongs to.</param>
        /// <param name="connectionInfo">ConnectionInfo object for the tabular model.</param>
        /// <param name="comparisonInfo">ComparisonInfo object for the tabular model.</param>
        public TabularModel(Comparison parentComparison, ConnectionInfo connectionInfo, ComparisonInfo comparisonInfo)
        {
            _parentComparison = parentComparison;
            _connectionInfo = connectionInfo;
            _comparisonInfo = comparisonInfo;
        }

        /// <summary>
        /// Connect to SSAS server and instantiate properties of the TabularModel object.
        /// </summary>
        public void Connect()
        {
            this.Disconnect();

            _server = new Server();
            _server.Connect($"DATA SOURCE={_connectionInfo.ServerName}");

            _database = _server.Databases.FindByName(_connectionInfo.DatabaseName);
            if (_database == null)
            {
                //Don't need try to load from project here as will already be done before instantiated Comparison
                throw new Amo.ConnectionException($"Could not connect to database {_connectionInfo.DatabaseName}");
            }

            //Shell model
            foreach (DataSource datasource in _database.Model.DataSources)
            {
                if (datasource.Type == DataSourceType.Provider)
                {
                    _connections.Add(new Connection(this, (ProviderDataSource)datasource));
                }
            }
            foreach (Tom.Table table in _database.Model.Tables)
            {
                _tables.Add(new Table(this, table));
            }
            foreach (ModelRole role in _database.Model.Roles)
            {
                //Workaround for AAD role members - todo delete if changed in Azure AS
                if (_parentComparison?.TargetTabularModel?.ConnectionInfo?.ServerName.Substring(0, 7) == "asazure")
                {
                    List<ExternalModelRoleMember> membersToAdd = new List<ExternalModelRoleMember>();
                    foreach (ModelRoleMember member in role.Members)
                    {
                        if (member is ExternalModelRoleMember && ((ExternalModelRoleMember)member).IdentityProvider == "AzureAD" && member.MemberID != null)
                        {
                            //AAD member from SSAS to Azure AS
                            ExternalModelRoleMember externalMemberOld = (ExternalModelRoleMember)member;
                            ExternalModelRoleMember externalMemberToAdd = new ExternalModelRoleMember();
                            externalMemberOld.CopyTo(externalMemberToAdd);
                            externalMemberToAdd.MemberID = null;
                            membersToAdd.Add(externalMemberToAdd);
                        }
                    }
                    foreach (ExternalModelRoleMember memberToAdd in membersToAdd)
                    {
                        role.Members.Remove(memberToAdd.Name);
                        role.Members.Add(memberToAdd);
                    }
                }
                else
                {
                    List<ExternalModelRoleMember> membersToAdd = new List<ExternalModelRoleMember>();
                    foreach (ModelRoleMember member in role.Members)
                    {
                        if (member is ExternalModelRoleMember && ((ExternalModelRoleMember)member).IdentityProvider == "AzureAD" && String.IsNullOrEmpty(member.MemberID))
                        {
                            //AAD member from Azure AS to SSAS
                            ExternalModelRoleMember externalMemberOld = (ExternalModelRoleMember)member;
                            ExternalModelRoleMember externalMemberToAdd = new ExternalModelRoleMember();
                            externalMemberOld.CopyTo(externalMemberToAdd);
                            externalMemberToAdd.MemberID = member.MemberName; //***
                            membersToAdd.Add(externalMemberToAdd);
                        }
                    }
                    foreach (ExternalModelRoleMember memberToAdd in membersToAdd)
                    {
                        role.Members.Remove(memberToAdd.Name);
                        role.Members.Add(memberToAdd);
                    }
                }

                _roles.Add(new Role(this, role));
            }
            foreach (Tom.Perspective perspective in _database.Model.Perspectives)
            {
                _perspectives.Add(new Perspective(this, perspective));
            }
            foreach (Tom.Culture culture in _database.Model.Cultures)
            {
                _cultures.Add(new Culture(this, culture));
            }
        }

        /// <summary>
        /// Disconnect from the SSAS server.
        /// </summary>
        public void Disconnect()
        {
            try
            {
                if (_server != null) _server.Disconnect();
            }
            catch { }
        }

        #region Properties

        /// <summary>
        /// Tabular Object Model Database object abtstracted by the TabularModel class.
        /// </summary>
        public Database TomDatabase
        {
            get { return _database; }
            set { _database = value; }
        }

        /// <summary>
        /// Collection of connections for the TabularModel object.
        /// </summary>
        public ConnectionCollection Connections => _connections;

        /// <summary>
        /// Collection of tables for the TabularModel object.
        /// </summary>
        public TableCollection Tables => _tables;

        /// <summary>
        /// Collection of perspectives for the TabularModel object.
        /// </summary>
        public PerspectiveCollection Perspectives => _perspectives;

        /// <summary>
        /// Collection of cultures for the TabularModel object.
        /// </summary>
        public CultureCollection Cultures => _cultures;

        /// <summary>
        /// Collection of roles for the TabularModel object.
        /// </summary>
        public RoleCollection Roles => _roles;

        /// <summary>
        /// ConnectionInfo object for the tabular model.
        /// </summary>
        public ConnectionInfo ConnectionInfo => _connectionInfo;

        /// <summary>
        /// ComparisonInfo object for the tabular model.
        /// </summary>
        public ComparisonInfo ComparisonInfo => _comparisonInfo;

        #endregion

        #region Relationships

        /// <summary>
        /// Check whether the TabularModel object contains a relationship.
        /// </summary>
        /// <param name="relationshipInternalName">The internal name of the relationship.</param>
        /// <returns>True if found; false if not.</returns>
        public bool ContainsRelationshipByInternalName(string relationshipInternalName)
        {
            bool foundRelationship = false;

            foreach (Table table in _tables)
            {
                foreach (Relationship relationship in table.Relationships)
                {
                    if (relationship.InternalName == relationshipInternalName)
                    {
                        foundRelationship = true;
                        break;
                    }
                }
                if (foundRelationship)
                {
                    break;
                }
            }

            return foundRelationship;
        }

        #endregion

        #region Connections

        /// <summary>
        /// Delete connection associated with the TabularModel object.
        /// </summary>
        /// <param name="name">The name of the connection to be deleted.</param>
        public void DeleteConnection(string name)
        {
            if (_database.Model.DataSources.ContainsName(name))
            {
                _database.Model.DataSources.Remove(name);
            }

            // shell model
            if (_connections.ContainsName(name))
            {
                _connections.RemoveByName(name);
            }
        }

        /// <summary>
        /// Create connection associated with the TabularModel object.
        /// </summary>
        /// <param name="connectionSource">Connection object from the source tabular model to be created in the target.</param>
        public void CreateConnection(Connection connectionSource)
        {
            ProviderDataSource tomConnectionTarget = new ProviderDataSource();
            connectionSource.TomConnection.CopyTo(tomConnectionTarget);

            _database.Model.DataSources.Add(tomConnectionTarget);

            // shell model
            _connections.Add(new Connection(this, tomConnectionTarget));
        }

        /// <summary>
        /// Update connection associated with the TabularModel object.
        /// </summary>
        /// <param name="connectionSource">Connection object from the source tabular model to be updated in the target.</param>
        /// <param name="connectionTarget">Connection object in the target tabular model to be updated.</param>
        public void UpdateConnection(Connection connectionSource, Connection connectionTarget)
        {
            connectionTarget.TomConnection.ConnectionString = connectionSource.TomConnection.ConnectionString;
            connectionTarget.TomConnection.ImpersonationMode = connectionSource.TomConnection.ImpersonationMode;
            connectionTarget.TomConnection.Account = connectionSource.TomConnection.Account;
            connectionTarget.TomConnection.Description = connectionSource.TomConnection.Description;
        }

        #endregion

        #region Tables

        /// <summary>
        /// Delete Table object, it's table in the underlying model, and all associated relationships.
        /// </summary>
        /// <param name="name">Name of the table to be deleted.</param>
        /// <returns>Collection of all associated relationships that had to be deleted. Useful if updating tables as then need to add back.</returns>
        public List<SingleColumnRelationship> DeleteTable(string name)
        {
            List<SingleColumnRelationship> deletedRelationships = new List<SingleColumnRelationship>();

            // shell model
            if (_tables.ContainsName(name))
            {
                deletedRelationships = _tables.FindByName(name).DeleteAllAssociatedRelationships();
                _tables.RemoveByName(name);
            }

            if (_database.Model.Tables.Contains(name))
            {
                _database.Model.Tables.Remove(name);
            }

            return deletedRelationships;
        }

        /// <summary>
        /// Create table associated with the TabularModel object.
        /// </summary>
        /// <param name="tableSource">Table object from the source tabular model to be created in the target.</param>
        public void CreateTable(Table tableSource)
        {
            Tom.Table tomTableTarget = new Tom.Table();
            tableSource.TomTable.CopyTo(tomTableTarget);

            //decouple from original model to the current one
            foreach (Partition partition in tomTableTarget.Partitions)
            {
                if (partition.SourceType == PartitionSourceType.Query)
                {
                    QueryPartitionSource queryPartition = ((QueryPartitionSource)partition.Source);
                    string dataSourceName = queryPartition.DataSource.Name;
                    queryPartition.DataSource = _connections.FindByName(dataSourceName).TomConnection;
                }
            }

            tomTableTarget.Measures.Clear();  //Measures will be added separately later

            _database.Model.Tables.Add(tomTableTarget);
            _tables.Add(new Table(this, tomTableTarget));
        }

        /// <summary>
        /// Update tablre associated with the TabularModel object.
        /// </summary>
        /// <param name="tableSource">Table object from the source tabular model to be updated in the target.</param>
        /// <param name="tableTarget">Table object in the target tabular model to be updated.</param>
        public void UpdateTable(Table tableSource, Table tableTarget)
        {
            Tom.Table tomTableTargetOrig = tableTarget.TomTable.Clone();

            List<SingleColumnRelationship> tomRelationshipsToAddBack = DeleteTable(tableTarget.Name);
            CreateTable(tableSource);

            //get back the newly created table
            tableTarget = _tables.FindByName(tableSource.Name);

            //add back deleted relationships where possible
            foreach (SingleColumnRelationship tomRelationshipToAddBack in tomRelationshipsToAddBack)
            {
                Table fromTable = _tables.FindByName(tomRelationshipToAddBack.FromTable.Name);
                Table toTable = _tables.FindByName(tomRelationshipToAddBack.ToTable.Name);

                if (fromTable != null && fromTable.TomTable.Columns.ContainsName(tomRelationshipToAddBack.FromColumn.Name) &&
                    toTable != null & toTable.TomTable.Columns.ContainsName(tomRelationshipToAddBack.ToColumn.Name))
                {
                    //decouple from original table to the current one
                    tomRelationshipToAddBack.FromColumn = fromTable.TomTable.Columns.Find(tomRelationshipToAddBack.FromColumn.Name);
                    tomRelationshipToAddBack.ToColumn = toTable.TomTable.Columns.Find(tomRelationshipToAddBack.ToColumn.Name);

                    fromTable.CreateRelationship(tomRelationshipToAddBack);
                }
            }

            //add back measures
            foreach (Tom.Measure tomMeasureToAddBack in tomTableTargetOrig.Measures)
            {
                tableTarget.CreateMeasure(tomMeasureToAddBack);
            }
        }

        #endregion

        #region Validation of Relationships

        /// <summary>
        /// Set relationships copied from source to inactive if cause ambigious paths.
        /// </summary>
        public void ValidateRelationships()
        {
            foreach (Table beginTable in _tables)
            {
                //beginTable might be the From or the To table in TOM Relationship object; it depends on CrossFilterDirection.

                RelationshipChain referencedTableCollection = new RelationshipChain();
                foreach (Relationship filteringRelationship in beginTable.FindFilteringRelationships())
                {
                    // EndTable can be either the From or the To table of a Relationship object depending on CrossFilteringBehavior
                    string endTableName = filteringRelationship.TomRelationship.FromTable.Name == beginTable.Name ? filteringRelationship.TomRelationship.ToTable.Name : filteringRelationship.TomRelationship.FromTable.Name;

                    RelationshipLink rootLink = new RelationshipLink(beginTable, _tables.FindByName(endTableName), true, "", filteringRelationship);
                    ValidateLink(rootLink, referencedTableCollection);
                }
            }

            //also issue a warning for any table that had it's guid name modified
            foreach (Table table in _tables)
            {
                foreach (Relationship relationship in table.Relationships)
                {
                    if (relationship.ModifiedInternalName)
                    {
                        _parentComparison.OnValidationMessage(new ValidationMessageEventArgs(
                            $"Relationship {relationship.Name.Trim()} has been created/updated, but its Name property was changed from \"{relationship.OldInternalName}\" to \"{relationship.InternalName}\" to avoid conflict with an existing relationship.",
                            ValidationMessageType.Relationship,
                            ValidationMessageStatus.Warning));
                    }
                }
            }
        }

        private void ValidateLink(RelationshipLink link, RelationshipChain chain)
        {
            if (chain.ContainsEndTableName(link.EndTable.Name))
            {
                // If we are here, we have identified 2 active paths to get to the same table.
                // So, the one that was already there in the target should win.

                RelationshipLink otherLink = chain.FindByEndTableName(link.EndTable.Name);
                string rootTableName = chain.FindRoot().BeginTable.Name;

                if (link.FilteringRelationship.CopiedFromSource)
                {
                    link.FilteringRelationship.TomRelationship.IsActive = false;

                    _parentComparison.OnValidationMessage(new ValidationMessageEventArgs(
                        $"Relationship {link.FilteringRelationship.Name.Trim()} (which is active in the source) has been created/updated, but is set to inactive in the target because it introduces ambiguous paths between '{rootTableName}' and '{link.EndTable.Name}': '{link.TablePath} and {otherLink.TablePath}'.",
                        ValidationMessageType.Relationship,
                        ValidationMessageStatus.Warning));
                }
                else
                {
                    // link.FilteringRelationship is the one that was already in the target.  So need to remove the other one that leads to the same table (which must have been copied from source)
                    otherLink.FilteringRelationship.TomRelationship.IsActive = false;

                    _parentComparison.OnValidationMessage(new ValidationMessageEventArgs(
                        $"Relationship {otherLink.FilteringRelationship.Name.Trim()} (which is active in the source) has been created/updated, but is set to inactive in the target because it introduces ambiguous paths between '{rootTableName}' and '{otherLink.EndTable.Name}': '{otherLink.TablePath} and {link.TablePath}'.",
                        ValidationMessageType.Relationship,
                        ValidationMessageStatus.Warning));

                    //remove OTHER ONE from collection as no longer in filtering chain
                    chain.RemoveByEndTableName(otherLink.EndTable.Name);
                }
            }

            if (link.FilteringRelationship.TomRelationship.IsActive)  //If not, we must have just set it to false above
            {
                //Add the link to the chain and re-iterate ...
                chain.Add(link);

                Table beginTable = link.EndTable; //EndTable is now the begin table as iterating next level ...
                foreach (Relationship filteringRelationship in beginTable.FindFilteringRelationships())
                {
                    // EndTable can be either the From or the To table of a Relationship object depending on direction of CrossFilteringBehavior
                    string endTableName = filteringRelationship.TomRelationship.FromTable.Name == beginTable.Name ? filteringRelationship.TomRelationship.ToTable.Name : filteringRelationship.TomRelationship.FromTable.Name;

                    //Need to check if endTableName has already been covered by TablePath to avoid CrossFilteringBehavior leading both ways and never ending loop
                    if (!link.TablePath.Contains("'" + endTableName + "'"))
                    {
                        RelationshipLink newLink = new RelationshipLink(beginTable, _tables.FindByName(endTableName), false, link.TablePath, filteringRelationship);
                        ValidateLink(newLink, chain);
                    }
                }
            }
        }

        #endregion

        /// <summary>
        /// Perspectives, cultures and roles will be affected by changes to tables, measures, etc. and can end up invalid. To avoid this, take a backup.
        /// </summary>
        public void BackupAffectedObjects()
        {
            _tomPerspectivesBackup = new List<Tom.Perspective>();
            foreach (Tom.Perspective perspective in _database.Model.Perspectives)
            {
                Tom.Perspective perspectiveToBackup = new Tom.Perspective();
                perspective.CopyTo(perspectiveToBackup);
                _tomPerspectivesBackup.Add(perspectiveToBackup);
            }

            _tomRolesBackup = new List<ModelRole>();
            foreach (ModelRole role in _database.Model.Roles)
            {
                ModelRole roleToBackup = new ModelRole();
                role.CopyTo(roleToBackup);
                _tomRolesBackup.Add(roleToBackup);
            }

            _tomCulturesBackup = new List<Tom.Culture>();
            foreach (Tom.Culture culture in _database.Model.Cultures)
            {
                Tom.Culture cultureToBackup = new Tom.Culture();
                culture.CopyTo(cultureToBackup);
                _tomCulturesBackup.Add(cultureToBackup);
            }
        }

        /// <summary>
        /// Restore perspectives after changes to tables, measures, etc.
        /// </summary>
        public void RestorePerspectives()
        {
            if (_tomPerspectivesBackup != null)
            {
                _database.Model.Perspectives.Clear();
                _perspectives.Clear();

                foreach (Tom.Perspective tomPerspective in _tomPerspectivesBackup)
                {
                    CreatePerspective(tomPerspective);
                }
            }
        }

        /// <summary>
        /// Restore roles after changes to tables, measures, etc.
        /// </summary>
        public void RestoreRoles()
        {
            if (_tomRolesBackup != null)
            {
                _database.Model.Roles.Clear();
                _roles.Clear();

                foreach (ModelRole tomRole in _tomRolesBackup)
                {
                    CreateRole(tomRole);
                }
            }
        }

        /// <summary>
        /// Restore perspectives after changes to tables, measures, etc. Note that perspectives need to be done and dusted before restore cultures or dependency error.
        /// </summary>
        public void RestoreCultues()
        {
            if (_tomCulturesBackup != null)
            {
                _database.Model.Cultures.Clear();
                _cultures.Clear();

                foreach (Tom.Culture tomCulture in _tomCulturesBackup)
                {
                    CreateCulture(tomCulture);
                }
            }
        }

        #region Perspectives

        /// <summary>
        /// Delete perspective associated with the TabularModel object.
        /// </summary>
        /// <param name="name">Name of the perspective to be deleted.</param>
        public void DeletePerspective(string name)
        {
            if (_database.Model.Perspectives.ContainsName(name))
            {
                _database.Model.Perspectives.Remove(name);
            }

            // shell model
            if (_perspectives.ContainsName(name))
            {
                _perspectives.Remove(name);
            }
        }

        /// <summary>
        /// Create perspective associated with the TabularModel object.
        /// </summary>
        /// <param name="tomPerspective">Tabular Object Model Perspective object from the source tabular model to be abstracted in the target.</param>
        /// <returns>Newly abstracted Tabular Object Model Perspective object.</returns>
        public Tom.Perspective CreatePerspective(Tom.Perspective tomPerspective)
        {
            Tom.Perspective tomPerspectiveTarget = new Tom.Perspective();
            tomPerspectiveTarget.Name = tomPerspective.Name;
            _database.Model.Perspectives.Add(tomPerspectiveTarget);
            _perspectives.Add(new Perspective(this, tomPerspectiveTarget));

            SyncPerspectives(tomPerspective, tomPerspectiveTarget);
            return tomPerspectiveTarget;
        }

        /// <summary>
        /// Update perspective associated with the TabularModel object.
        /// </summary>
        /// <param name="tomPerspectiveSource">Tabular Object Model Perspective object from the source tabular model to be abstracted in the target.</param>
        /// <param name="tomPerspectiveTarget">Tabular Object Model Perspective object in the target tabular model to be abstracted.</param>
        public void UpdatePerspective(Tom.Perspective tomPerspectiveSource, Tom.Perspective tomPerspectiveTarget)
        {
            if (_comparisonInfo.OptionsInfo.OptionMergePerspectives)
            {
                Tom.Perspective perspectiveBackup = null;
                foreach (Tom.Perspective perspective in _tomPerspectivesBackup)
                {
                    if (perspective.Name == tomPerspectiveTarget.Name)
                    {
                        perspectiveBackup = perspective;
                        break;
                    }
                }

                if (perspectiveBackup != null)
                {
                    DeletePerspective(tomPerspectiveTarget.Name);
                    Tom.Perspective perspectiveTargetNew = CreatePerspective(perspectiveBackup);

                    SyncPerspectives(tomPerspectiveSource, perspectiveTargetNew);
                }
            }
            else
            {
                DeletePerspective(tomPerspectiveTarget.Name);
                CreatePerspective(tomPerspectiveSource);
            }
        }

        private void SyncPerspectives(Tom.Perspective perspectiveSource, Tom.Perspective perspectiveTarget)
        {
            //Tables
            foreach (PerspectiveTable perspectiveTableSource in perspectiveSource.PerspectiveTables)
            {
                Table tableTarget = _tables.FindByName(perspectiveTableSource.Name);
                if (tableTarget != null)
                {
                    //Following line is returning null in CTP3.3, when it shouldn't, so having to iterate to find
                    //PerspectiveTable perspectiveTableTarget = perspectiveTarget.PerspectiveTables.Find(perspectiveTableSource.Name); //When merging perspectives, might already be there
                    PerspectiveTable perspectiveTableTarget = null;
                    foreach (PerspectiveTable table in perspectiveTarget.PerspectiveTables)
                    {
                        if (table.Name == perspectiveTableSource.Name)
                        {
                            perspectiveTableTarget = table;
                            break;
                        }
                    }

                    if (perspectiveTableTarget == null)
                    {
                        perspectiveTableTarget = new PerspectiveTable();
                        perspectiveTarget.PerspectiveTables.Add(perspectiveTableTarget);
                        perspectiveTableTarget.Name = perspectiveTableSource.Name;
                        perspectiveTableTarget.Table = tableTarget.TomTable;
                    }

                    //Columns
                    foreach (PerspectiveColumn perspectiveColumnSource in perspectiveTableSource.PerspectiveColumns)
                    {
                        Column column = tableTarget.TomTable.Columns.Find(perspectiveColumnSource.Name);
                        if (column != null)
                        {
                            //Following line is returning null in CTP3.3, when it shouldn't, so having to iterate to find
                            //PerspectiveColumn perspectiveColumnTarget = perspectiveTarget.PerspectiveColumns.Find(perspectiveColumnSource.Name); //When merging perspectives, might already be there
                            PerspectiveColumn perspectiveColumnTarget = null;
                            foreach (PerspectiveColumn perspectiveColumn in perspectiveTableTarget.PerspectiveColumns)
                            {
                                if (perspectiveColumn.Name == perspectiveColumnSource.Name)
                                {
                                    perspectiveColumnTarget = perspectiveColumn;
                                    break;
                                }
                            }

                            if (perspectiveColumnTarget == null)
                            {
                                perspectiveColumnTarget = new PerspectiveColumn();
                                perspectiveTableTarget.PerspectiveColumns.Add(perspectiveColumnTarget);
                                perspectiveColumnTarget.Name = perspectiveColumnSource.Name;
                                perspectiveColumnTarget.Column = column;
                            }
                        }
                    }

                    //Hierarchies
                    foreach (PerspectiveHierarchy perspectiveHierarchySource in perspectiveTableSource.PerspectiveHierarchies)
                    {
                        Hierarchy hierarchy = tableTarget.TomTable.Hierarchies.Find(perspectiveHierarchySource.Name);
                        if (hierarchy != null)
                        {
                            //Following line is returning null in CTP3.3, when it shouldn't, so having to iterate to find
                            //PerspectiveHierarchy perspectiveHierarchyTarget = perspectiveTarget.PerspectiveHierarchies.Find(perspectiveHierarchySource.Name); //When merging perspectives, might already be there
                            PerspectiveHierarchy perspectiveHierarchyTarget = null;
                            foreach (PerspectiveHierarchy perspectiveHierarchy in perspectiveTableTarget.PerspectiveHierarchies)
                            {
                                if (perspectiveHierarchy.Name == perspectiveHierarchySource.Name)
                                {
                                    perspectiveHierarchyTarget = perspectiveHierarchy;
                                    break;
                                }
                            }

                            if (perspectiveHierarchyTarget == null)
                            {
                                perspectiveHierarchyTarget = new PerspectiveHierarchy();
                                perspectiveTableTarget.PerspectiveHierarchies.Add(perspectiveHierarchyTarget);
                                perspectiveHierarchyTarget.Name = perspectiveHierarchySource.Name;
                                perspectiveHierarchyTarget.Hierarchy = hierarchy;
                            }
                        }
                    }

                    //Measures
                    foreach (PerspectiveMeasure perspectiveMeasureSource in perspectiveTableSource.PerspectiveMeasures)
                    {
                        Tom.Measure measure = tableTarget.TomTable.Measures.Find(perspectiveMeasureSource.Name);
                        if (measure != null)
                        {
                            //Following line is returning null in CTP3.3, when it shouldn't, so having to iterate to find
                            //PerspectiveMeasure perspectiveMeasureTarget = perspectiveTarget.PerspectiveMeasures.Find(perspectiveMeasureSource.Name); //When merging perspectives, might already be there
                            PerspectiveMeasure perspectiveMeasureTarget = null;
                            foreach (PerspectiveMeasure perspectiveMeasure in perspectiveTableTarget.PerspectiveMeasures)
                            {
                                if (perspectiveMeasure.Name == perspectiveMeasureSource.Name)
                                {
                                    perspectiveMeasureTarget = perspectiveMeasure;
                                    break;
                                }
                            }

                            if (perspectiveMeasureTarget == null)
                            {
                                perspectiveMeasureTarget = new PerspectiveMeasure();
                                perspectiveTableTarget.PerspectiveMeasures.Add(perspectiveMeasureTarget);
                                perspectiveMeasureTarget.Name = perspectiveMeasureSource.Name;
                                perspectiveMeasureTarget.Measure = measure;
                            }
                        }
                    }
                }
            }
        }

        #endregion

        #region Cultures

        /// <summary>
        /// Delete culture associated with the TabularModel object.
        /// </summary>
        /// <param name="name">Name of the culture to be deleted.</param>
        public void DeleteCulture(string name)
        {
            if (_database.Model.Cultures.ContainsName(name))
            {
                _database.Model.Cultures.Remove(name);
            }

            // shell model
            if (_cultures.ContainsName(name))
            {
                _cultures.Remove(name);
            }
        }

        /// <summary>
        /// Create culture associated with the TabularModel object.
        /// </summary>
        /// <param name="tomCulture">Tabular Object Model Culture object from the source tabular model to be abstracted in the target.</param>
        /// <returns>Newly abstracted Tabular Object Model Culture object.</returns>
        public Tom.Culture CreateCulture(Tom.Culture tomCulture)
        {
            Tom.Culture tomCultureTarget = new Tom.Culture();
            tomCultureTarget.Name = tomCulture.Name;
            _database.Model.Cultures.Add(tomCultureTarget);
            _cultures.Add(new Culture(this, tomCultureTarget));

            SyncCultures(tomCulture, tomCultureTarget);
            return tomCultureTarget;
        }

        /// <summary>
        /// Update culture associated with the TabularModel object.
        /// </summary>
        /// <param name="tomCultureSource">Tabular Object Model Culture object from the source tabular model to be abstracted in the target.</param>
        /// <param name="tomCultureTarget">Tabular Object Model Culture object in the target tabular model to be abstracted.</param>
        public void UpdateCulture(Tom.Culture tomCultureSource, Tom.Culture tomCultureTarget)
        {
            if (_comparisonInfo.OptionsInfo.OptionMergeCultures)
            {
                Tom.Culture tomCultureBackup = null;
                foreach (Tom.Culture tomCulture in _tomCulturesBackup)
                {
                    if (tomCulture.Name == tomCultureTarget.Name)
                    {
                        tomCultureBackup = tomCulture;
                        break;
                    }
                }

                if (tomCultureBackup != null)
                {
                    DeleteCulture(tomCultureTarget.Name);
                    Tom.Culture tomCultureTargetNew = CreateCulture(tomCultureBackup);

                    SyncCultures(tomCultureSource, tomCultureTargetNew);
                }
            }
            else
            {
                DeleteCulture(tomCultureTarget.Name);
                CreateCulture(tomCultureSource);
            }
        }

        private void SyncCultures(Tom.Culture tomCultureSource, Tom.Culture tomCultureTarget)
        {
            foreach (ObjectTranslation translationSource in tomCultureSource.ObjectTranslations)
            {
                if (translationSource.Object is NamedMetadataObject)
                {
                    NamedMetadataObject namedObjectSource = (NamedMetadataObject)translationSource.Object;
                    NamedMetadataObject namedObjectTarget = null;

                    //Find the object in the target model that this translation applies to
                    switch (namedObjectSource.ObjectType)
                    {
                        case ObjectType.Model:
                            //if (namedObjectSource.Name == tomCultureTarget.Model.Name)
                            //{
                            //Model name can legitimately have different names - and there can only be 1 model, so we are OK not doing check in if clause above.
                            namedObjectTarget = tomCultureTarget.Model;
                            //}
                            break;
                        case ObjectType.Table:
                            foreach (Tom.Table tomTableTarget in tomCultureTarget.Model.Tables)
                            {
                                if (namedObjectSource.Name == tomTableTarget.Name)
                                {
                                    namedObjectTarget = tomTableTarget;
                                    break;
                                }
                            }
                            break;
                        case ObjectType.Column:
                            Column columnSource = (Column)namedObjectSource;
                            foreach (Tom.Table tableTarget in tomCultureTarget.Model.Tables)
                            {
                                bool foundColumn = false;
                                if (columnSource.Table?.Name == tableTarget.Name)
                                {
                                    foreach (Column columnTarget in tableTarget.Columns)
                                    {
                                        if (columnSource.Name == columnTarget.Name)
                                        {
                                            namedObjectTarget = columnTarget;
                                            foundColumn = true;
                                            break;
                                        }
                                    }
                                }
                                if (foundColumn) break;
                            }
                            break;
                        case ObjectType.Measure:
                            Tom.Measure tomMeasureSource = (Tom.Measure)namedObjectSource;
                            foreach (Tom.Table tomTableTarget in tomCultureTarget.Model.Tables)
                            {
                                bool foundMeasure = false;
                                if (tomMeasureSource.Table?.Name == tomTableTarget.Name)
                                {
                                    foreach (Tom.Measure measureTarget in tomTableTarget.Measures)
                                    {
                                        if (tomMeasureSource.Name == measureTarget.Name)
                                        {
                                            namedObjectTarget = measureTarget;
                                            foundMeasure = true;
                                            break;
                                        }
                                    }
                                }
                                if (foundMeasure) break;
                            }
                            break;
                        case ObjectType.Hierarchy:
                            Hierarchy hierarchySource = (Hierarchy)namedObjectSource;
                            foreach (Tom.Table tomTableTarget in tomCultureTarget.Model.Tables)
                            {
                                bool foundHierarchy = false;
                                if (hierarchySource.Table?.Name == tomTableTarget.Name)
                                {
                                    foreach (Hierarchy hierarchyTarget in tomTableTarget.Hierarchies)
                                    {
                                        if (hierarchySource.Name == hierarchyTarget.Name)
                                        {
                                            namedObjectTarget = hierarchyTarget;
                                            foundHierarchy = true;
                                            break;
                                        }
                                    }
                                }
                                if (foundHierarchy) break;
                            }
                            break;
                        case ObjectType.Level:
                            Level levelSource = (Level)namedObjectSource;
                            foreach (Tom.Table tomTableTarget in tomCultureTarget.Model.Tables)
                            {
                                bool foundLevel = false;
                                if (levelSource.Hierarchy?.Table?.Name == tomTableTarget.Name)
                                {
                                    foreach (Hierarchy hierarchyTarget in tomTableTarget.Hierarchies)
                                    {
                                        if (levelSource.Hierarchy?.Name == hierarchyTarget.Name)
                                        {
                                            foreach (Level levelTarget in hierarchyTarget.Levels)
                                            {
                                                if (levelSource.Name == levelTarget.Name)
                                                {
                                                    namedObjectTarget = levelTarget;
                                                    foundLevel = true;
                                                    break;
                                                }
                                            }
                                        }
                                        if (foundLevel) break;
                                    }
                                    if (foundLevel) break;
                                }
                            }
                            break;
                        case ObjectType.Perspective:
                            foreach (Tom.Perspective tomPerspectiveTarget in tomCultureTarget.Model.Perspectives)
                            {
                                if (namedObjectSource.Name == tomPerspectiveTarget.Name)
                                {
                                    namedObjectTarget = tomPerspectiveTarget;
                                    break;
                                }
                            }
                            break;
                        case ObjectType.Role:
                            foreach (ModelRole tomRoleTarget in tomCultureTarget.Model.Roles)
                            {
                                if (namedObjectSource.Name == tomRoleTarget.Name)
                                {
                                    namedObjectTarget = tomRoleTarget;
                                    break;
                                }
                            }
                            break;
                        //case ObjectType.KPI:  //KPIs dealt with by measures above
                        //    break;
                        default:
                            break;
                    }

                    //todo delete
                    ////Not sure why switch statement not stopping on "case ObjectType.Perspective:" above. Fudge below. Todo2: test with later build.
                    //if (namedObjectSource.ObjectType.ToString() == "Perspective")
                    //{
                    //    foreach (Tom.Perspective tomPerspectiveTarget in tomCultureTarget.Model.Perspectives)
                    //    {
                    //        if (namedObjectSource.Name == tomPerspectiveTarget.Name)
                    //        {
                    //            namedObjectTarget = tomPerspectiveTarget;
                    //            break;
                    //        }
                    //    }
                    //}

                    //If namedObjectTarget is null, the model object doesn't exist in target, so can ignore
                    if (namedObjectTarget != null)
                    {
                        //Does the translation already exist in cultureTarget?
                        ObjectTranslation translationTarget = null;
                        foreach (ObjectTranslation translation in tomCultureTarget.ObjectTranslations)
                        {
                            if (translation.Object is NamedMetadataObject &&
                                ((NamedMetadataObject)translation.Object).Name == namedObjectSource.Name &&
                                translation.Object.ObjectType == namedObjectSource.ObjectType &&
                                translation.Property == translationSource.Property
                               )
                            {
                                translationTarget = translation;
                                break;
                            }
                        }

                        if (translationTarget != null)
                        {   //Translation already exists in cultureTarget for this object, so just ensure values match
                            //Also decouple from object in model and reset coupling
                            translationTarget.Object = namedObjectTarget;
                            translationTarget.Value = translationSource.Value;
                        }
                        else
                        {   //Translation does not exist in cultureTarget, so create it and add it to culture
                            translationTarget = new ObjectTranslation();
                            translationTarget.Object = namedObjectTarget;
                            translationTarget.Property = translationSource.Property;
                            translationTarget.Value = translationSource.Value;
                            tomCultureTarget.ObjectTranslations.Add(translationTarget);
                        }
                    }
                }
            }
        }

        #endregion

        #region Roles

        /// <summary>
        /// Delete role associated with the TabularModel object.
        /// </summary>
        /// <param name="name">Name of the role to be deleted.</param>
        public void DeleteRole(string name)
        {
            if (_database.Model.Roles.Contains(name))
            {
                _database.Model.Roles.Remove(name);
            }

            // shell model
            if (_roles.ContainsName(name))
            {
                _roles.Remove(name);
            }
        }

        /// <summary>
        /// Create role associated with the TabularModel object.
        /// </summary>
        /// <param name="tomRoleSource">Tabular Object Model ModelRole object from the source tabular model to be abstracted in the target.</param>
        public void CreateRole(ModelRole tomRoleSource)
        {
            ModelRole tomRoleTarget = new ModelRole();
            tomRoleSource.CopyTo(tomRoleTarget);

            List<string> permissionNamesToDelete = new List<string>();
            foreach (TablePermission permission in tomRoleTarget.TablePermissions)
            {
                if (_tables.ContainsName(permission.Table.Name))
                {
                    //decouple table permissions from from original table to the one in target model (if exists)
                    permission.Table = _tables.FindByName(permission.Table.Name).TomTable;
                }
                else
                {
                    permissionNamesToDelete.Add(permission.Name);
                }
            }
            foreach (string name in permissionNamesToDelete)
            {
                tomRoleTarget.TablePermissions.Remove(name);
            }

            _database.Model.Roles.Add(tomRoleTarget);

            // shell model
            _roles.Add(new Role(this, tomRoleTarget));
        }

        /// <summary>
        /// Update role associated with the TabularModel object.
        /// </summary>
        /// <param name="roleSource">Role object from the source tabular model to be updated in the target.</param>
        /// <param name="roleTarget">Role object in the target tabular model to be updated.</param>
        public void UpdateRole(Role roleSource, Role roleTarget)
        {
            DeleteRole(roleTarget.Name);
            CreateRole(roleSource.TomRole);
        }

        /// <summary>
        /// Remove any role references to non-existing tables.
        /// </summary>
        public void RolesCleanup()
        {
            //Check for roles' table permissions referring to non-existing tables
            foreach (ModelRole tomRole in _database.Model.Roles)
            {
                List<string> permissionNamesToDelete = new List<string>();

                foreach (TablePermission permission in tomRole.TablePermissions)
                {
                    if (permission.Table == null || !_tables.ContainsName(permission.Table.Name))
                    {
                        permissionNamesToDelete.Add(permission.Name);
                    }
                }

                foreach (string name in permissionNamesToDelete)
                {
                    tomRole.TablePermissions.Remove(name);
                }
            }
        }

        #endregion

        /// <summary>
        /// Update target tabular model with changes resulting from the comparison. For database deployment, this will fire DeployDatabaseCallBack.
        /// </summary>
        /// <returns>Boolean indicating whether update was successful.</returns>
        public bool Update()
        {
            FinalValidation();

            if (_connectionInfo.UseProject)
            {
                UpdateProject();
            }
            else
            {   //Database deployement

                if (_comparisonInfo.PromptForDatabaseProcessing)
                {
                    //Call back to show deployment form
                    DatabaseDeploymentEventArgs args = new DatabaseDeploymentEventArgs();
                    _parentComparison.OnDatabaseDeployment(args);
                    return args.DeploymentSuccessful;
                }
                else
                {
                    //Simple update target without setting passwords or processing (mainly for command-line execution)
                    UpdateWithScript();
                }
            }

            return true;
        }

        private void UpdateProject()
        {
            UpdateWithScript();

            if (_connectionInfo.Project != null)
            {
                //Running in VS
                EnvDTE._DTE dte = _connectionInfo.Project.DTE;

                //check out bim file if necessary
                if (dte.SourceControl.IsItemUnderSCC(_connectionInfo.BimFileFullName) && !dte.SourceControl.IsItemCheckedOut(_connectionInfo.BimFileFullName))
                {
                    dte.SourceControl.CheckOutItem(_connectionInfo.BimFileFullName);
                }
            }

            //Script out db and write to project file

            //serialize db to json
            SerializeOptions options = new SerializeOptions();
            options.IgnoreInferredProperties = true;
            options.IgnoreInferredObjects = true;
            options.IgnoreTimestamps = true;
            options.SplitMultilineStrings = true;
            string json = JsonSerializer.SerializeDatabase(_database, options);

            //replace db name with "SemanticModel"
            JObject jDb = JObject.Parse(json);
            jDb["name"] = "SemanticModel";
            jDb["id"] = "SemanticModel";
            json = jDb.ToString();

            File.WriteAllText(_connectionInfo.BimFileFullName, json);
        }

        #region Database deployment and processing methods

        private const string _deployRowWorkItem = "Deploy metadata";
        private ProcessingTableCollection _tablesToProcess;

        /// <summary>
        /// Perform database deployment and processing of required tables.
        /// </summary>
        /// <param name="tablesToProcess">Tables to process.</param>
        public void DatabaseDeployAndProcess(ProcessingTableCollection tablesToProcess)
        {
            try
            {
                _tablesToProcess = tablesToProcess;

                //Todo: do passwords first, then UpdateWithScript(), then add passwords back, then OnDeloymentMessage, so can back out of deployment
                UpdateWithScript();

                //Set passwords ready for processing
                foreach (DataSource dataSource in _database.Model.DataSources)
                {
                    if (dataSource.Type == DataSourceType.Provider)
                    {
                        ProviderDataSource providerDataSource = (ProviderDataSource)dataSource;

                        if (providerDataSource.ImpersonationMode == ImpersonationMode.ImpersonateAccount)
                        {
                            PasswordPromptEventArgs args = new PasswordPromptEventArgs();
                            args.ConnectionName = dataSource.Name;
                            args.Username = providerDataSource.Account;
                            _parentComparison.OnPasswordPrompt(args);
                            if (args.UserCancelled)
                            {
                                // Show cancelled for all rows
                                _parentComparison.OnDeploymentMessage(new DeploymentMessageEventArgs(_deployRowWorkItem, "Deployment has been cancelled.", DeploymentStatus.Cancel));
                                foreach (ProcessingTable table in _tablesToProcess)
                                {
                                    _parentComparison.OnDeploymentMessage(new DeploymentMessageEventArgs(table.Name, "Cancelled", DeploymentStatus.Cancel));
                                }
                                _parentComparison.OnDeploymentComplete(new DeploymentCompleteEventArgs(DeploymentStatus.Cancel, null));
                                return;
                            }
                            providerDataSource.Account = args.Username;
                            providerDataSource.Password = args.Password;
                        }
                    }
                }

                _parentComparison.OnDeploymentMessage(new DeploymentMessageEventArgs(_deployRowWorkItem, "Success. Metadata deployed.", DeploymentStatus.Success));
                ProcessAsyncDelegate processAsyncCaller = new ProcessAsyncDelegate(Process);
                processAsyncCaller.BeginInvoke(null, null);

                _parentComparison.OnDeploymentComplete(new DeploymentCompleteEventArgs(DeploymentStatus.Success, null));
            }
            catch (Exception exc)
            {
                _parentComparison.OnDeploymentMessage(new DeploymentMessageEventArgs(_deployRowWorkItem, "Error deploying metadata.", DeploymentStatus.Error));
                foreach (ProcessingTable table in _tablesToProcess)
                {
                    _parentComparison.OnDeploymentMessage(new DeploymentMessageEventArgs(table.Name, "Error", DeploymentStatus.Error));
                }
                _parentComparison.OnDeploymentComplete(new DeploymentCompleteEventArgs(DeploymentStatus.Error, exc.Message));
            }
        }

        private void UpdateWithScript()
        {
            //_database.Update(Amo.UpdateOptions.ExpandFull); //If make minor changes (e.g. display folder) to table without changes to the partition or column structure, this command will still lose the data due to previous operations, so reconnect and run script instead

            string tmslCommand = JsonScripter.ScriptCreateOrReplace(_database);

            _server.Disconnect();
            _server = new Server();
            _server.Connect("DATA SOURCE=" + _connectionInfo.ServerName);
            Amo.XmlaResultCollection results = _server.Execute(tmslCommand);
            if (results.ContainsErrors)
                throw new Amo.OperationException(results);

            _database = _server.Databases.FindByName(_connectionInfo.DatabaseName);

            //From this point onwards use only TOM as have not bothered re-hydrating the BismNorm object model
        }

        private bool _stopProcessing;
        string _sessionId;
        internal delegate void ProcessAsyncDelegate();
        private void Process()
        {
            //string x13 = TraceColumn.ObjectName.ToString();
            //string x15 = TraceColumn.ObjectReference.ToString();
            //string x10 = TraceColumn.IntegerData.ToString();

            Trace trace = null;
            try
            {
                _stopProcessing = false;
                RefreshType refreshType = _comparisonInfo.OptionsInfo.OptionProcessingOption == ProcessingOption.Default ? RefreshType.Automatic : RefreshType.Full;

                //Set up server trace to capture how many rows processed
                _sessionId = _server.SessionID;
                trace = _server.Traces.Add();
                TraceEvent traceEvent = trace.Events.Add(Amo.TraceEventClass.ProgressReportCurrent);
                traceEvent.Columns.Add(Amo.TraceColumn.ObjectID);
                traceEvent.Columns.Add(Amo.TraceColumn.ObjectName);
                traceEvent.Columns.Add(Amo.TraceColumn.ObjectReference);
                traceEvent.Columns.Add(Amo.TraceColumn.IntegerData);
                traceEvent.Columns.Add(Amo.TraceColumn.SessionID);
                traceEvent.Columns.Add(Amo.TraceColumn.Spid);
                trace.Update();
                trace.OnEvent += new TraceEventHandler(Trace_OnEvent);
                trace.Start();

                if (_tablesToProcess.Count > 0)
                {
                    foreach (ProcessingTable tableToProcess in _tablesToProcess)
                    {
                        Tom.Table table = _database.Model.Tables.Find(tableToProcess.Name);
                        if (table != null)
                        {
                            table.RequestRefresh(refreshType);
                        }
                    }
                }
                else
                {
                    //Need this in case created new relationships but no tables
                    _database.Model.RequestRefresh(RefreshType.Calculate);
                }
                _database.Model.SaveChanges();

                // Show row count for each table
                foreach (ProcessingTable table in _tablesToProcess)
                {
                    int rowCount = _connectionInfo.DirectQuery ? 0 : Core.Comparison.FindRowCount(_server, table.Name, _database.Name);
                    _parentComparison.OnDeploymentMessage(new DeploymentMessageEventArgs(table.Name, $"Success. {String.Format("{0:#,###0}", rowCount)} rows transferred.", DeploymentStatus.Success));
                }
                _parentComparison.OnDeploymentComplete(new DeploymentCompleteEventArgs(DeploymentStatus.Success, null));
            }
            catch (Exception exc)
            {
                ShowErrorsForAllRows();
                _parentComparison.OnDeploymentComplete(new DeploymentCompleteEventArgs(DeploymentStatus.Error, exc.Message));
            }
            finally
            {
                try
                {
                    trace.Stop();
                    trace.Drop();
                }
                catch { }
            }
        }

        /// <summary>
        /// Stop processing if possible.
        /// </summary>
        public void StopProcessing()
        {
            _stopProcessing = true;

            if (_comparisonInfo.OptionsInfo.OptionTransaction)
            {
                try
                {
                    _server.RollbackTransaction();
                    ShowErrorsForAllRows();
                    _parentComparison.OnDeploymentComplete(new DeploymentCompleteEventArgs(DeploymentStatus.Error, "Rolled back transaction."));
                }
                catch (Exception exc)
                {
                    if (exc is NullReferenceException || exc is InvalidOperationException)
                    {
                        return;
                    }
                    else
                    {
                        ShowErrorsForAllRows();
                        _parentComparison.OnDeploymentComplete(new DeploymentCompleteEventArgs(DeploymentStatus.Error, exc.Message));
                    }
                }
            }
        }

        private void Trace_OnEvent(object sender, TraceEventArgs e)
        {
            if (e.ObjectName != null && e.ObjectReference != null && e.SessionID == _sessionId)
            {
                XmlDocument document = new XmlDocument();
                document.LoadXml(e.ObjectReference);

                XmlNodeList partitionNodeList = document.GetElementsByTagName("Partition");
                XmlNodeList tableNodeList = document.GetElementsByTagName("Table");

                if (partitionNodeList != null && tableNodeList != null)
                {
                    if (_tablesToProcess.ContainsId(tableNodeList[0].InnerText))
                    {
                        ProcessingTable processingTable = _tablesToProcess.FindById(tableNodeList[0].InnerText);

                        if (!processingTable.ContainsPartition(partitionNodeList[0].InnerText))
                        {
                            processingTable.Partitions.Add(new PartitionRowCounter(partitionNodeList[0].InnerText));
                        }

                        PartitionRowCounter partition = processingTable.FindPartition(partitionNodeList[0].InnerText);
                        partition.RowCount = e.IntegerData;

                        _parentComparison.OnDeploymentMessage(new DeploymentMessageEventArgs(processingTable.Name, $"Retreived {String.Format("{0:#,###0}", processingTable.GetRowCount())} rows ...", DeploymentStatus.Deploying));
                    }
                }

                if (_stopProcessing && !_comparisonInfo.OptionsInfo.OptionTransaction) //transactions get cancelled in StopProcessing, not here
                {
                    try
                    {
                        _server.CancelCommand(_sessionId);
                    }
                    catch { }
                }

                ////Doesn't work with Spid, so doing sessionid above
                //int spid;
                //if (_stopProcessing && int.TryParse(e.Spid, out spid))
                //{
                //    try
                //    {
                //        //_amoServer.CancelCommand(e.Spid);
                //        string commandStatement = String.Format("<Cancel xmlns=\"http://schemas.microsoft.com/analysisservices/2003/engine\"><SPID>{0}</SPID><CancelAssociated>1</CancelAssociated></ Cancel>", e.Spid);
                //        System.Diagnostics.Debug.WriteLine(commandStatement);
                //        _amoServer.Execute(commandStatement);
                //        //_connectionInfo.ExecuteXmlaCommand(_amoServer, commandStatement);
                //    }
                //    catch { }
                //}
            }
        }

        private void ShowErrorsForAllRows()
        {
            // Show error for each item
            if (_comparisonInfo.OptionsInfo.OptionTransaction)
            {
                _parentComparison.OnDeploymentMessage(new DeploymentMessageEventArgs(_deployRowWorkItem, "Error", DeploymentStatus.Error));
            }
            foreach (ProcessingTable table in _tablesToProcess)
            {
                _parentComparison.OnDeploymentMessage(new DeploymentMessageEventArgs(table.Name, "Error", DeploymentStatus.Error));
            }
        }

        #endregion

        /// <summary>
        /// Generate script containing full tabular model definition.
        /// </summary>
        /// <returns>JSON script of tabular model defintion.</returns>
        public string ScriptDatabase()
        {
            FinalValidation();

            //script db to json
            string json = JsonScripter.ScriptCreateOrReplace(_database);

            if (_connectionInfo.UseProject)
            {
                //replace db/cube name/id with name of deploymnet db from the project file
                JObject jScript = JObject.Parse(json);
                JObject createOrReplace = (JObject)jScript["createOrReplace"];
                if (!String.IsNullOrEmpty(_connectionInfo.DeploymentServerDatabase))
                {
                    ((JObject)createOrReplace["object"])["database"] = _connectionInfo.DeploymentServerDatabase;
                    ((JObject)createOrReplace["database"])["name"] = _connectionInfo.DeploymentServerDatabase;
                    ((JObject)createOrReplace["database"])["id"] = _connectionInfo.DeploymentServerDatabase;
                }
                json = jScript.ToString();
            }

            return json;
        }

        private void FinalValidation()
        {
            if (_connectionInfo.DirectQuery && _connections.Count > 1)
            {
                throw new InvalidOperationException("Target model contains multiple connections, which are not allowed for Direct Query models. Re-run comparison and (considering changes) ensure there is a single connection in the target model.");
            }
        }

        public override string ToString() => this.GetType().FullName;

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    if (_server != null)
                    {
                        _server.Dispose();
                    }
                }

                _disposed = true;
            }
        }

    }
}

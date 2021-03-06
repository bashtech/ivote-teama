﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.UI;
using System.Web.UI.WebControls;
using System.Web.UI.HtmlControls;
using System.Data;
using System.Web.Security;

using DatabaseEntities;
using FluentNHibernate;
using FluentNHibernate.Cfg;
using FluentNHibernate.Cfg.Db;
using NHibernate.Tool.hbm2ddl;
using NHibernate;
using NHibernate.Criterion;
using NHibernate.Cfg;

public partial class committee_election : System.Web.UI.Page
{
    private Committee committee;
    private CommitteeElection election;
    private DatabaseEntities.User user;
    private int ElectionID;
    
    private ISession session;

    protected void Page_Load(object sender, EventArgs e)
    {
        if (Request.QueryString["id"] == null ||
            Request.QueryString["id"] == "")
            throw new HttpException(400, "Invalid election ID");

        ElectionID = int.Parse(Request.QueryString["id"]);

        session = NHibernateHelper.CreateSessionFactory().OpenSession();

        // grab the objects based off the committee ID
        election = CommitteeElection.FindElection(session, ElectionID);
        if (election == null)
            Response.Redirect("home.aspx#election_not_found");
        committee = Committee.FindCommittee(session, election.PertinentCommittee);
        if (committee == null)
            Response.Redirect("home.aspx#committee_not_found");
        
        VacancyCount.Text = election.VacanciesToFill.ToString();
        CommitteeNameLiteral.Text = committee.Name;
        CommitteeNameLiteral2.Text = committee.Name;
        CommitteeDescription.Text = committee.Description;
        must_be_tenured.Visible = committee.TenureRequired;

        user = DatabaseEntities.User.FindUser(session, User.Identity.Name);
        
        MakeElectionSpecial.Visible = user.IsAdmin;

        // expose the pertinent panel based on the state of the election.
        switch (election.Phase)
        {
            case ElectionPhase.WTSPhase:
                //*******************************
                //****** Faculty WTS Load *******
                //*******************************
                //Check if WTS already exists
                List<DatabaseEntities.CommitteeWTS> wtsList = DatabaseEntities.CommitteeWTS.FindCommitteeWTS(session, election.ID);
                bool wtsAlreadySubmitted = false;
                foreach (DatabaseEntities.CommitteeWTS wts in wtsList)
                {
                    if (wts.Election == election.ID && wts.User == user.ID &&
                        (!committee.TenureRequired || user.IsTenured) &&
                        (!committee.BargainingUnitRequired || user.IsBargainingUnit))
                        wtsAlreadySubmitted = true;
                }

                if(user.CurrentCommittee == committee.ID) {
                    wtsPanelServing.Visible = true;
                    wtsPanelNew.Visible = false;
                } else if (wtsAlreadySubmitted &&
                   (!committee.TenureRequired || user.IsTenured) &&
                   (!committee.BargainingUnitRequired || user.IsBargainingUnit))
                {
                    wtsPanelExisting.Visible = true;
                    wtsPanelNew.Visible = false;
                }
                if ((!committee.TenureRequired || user.IsTenured) &&
                    (!committee.BargainingUnitRequired || user.IsBargainingUnit))
                    FacultyWTS.Visible = true;
                //Prevent a user from submitting a WTS if a member from their department is on the committee
                if(DatabaseEntities.Committee.DepartmentRepresented(session, committee, user.Department))
                    FacultyWTS.Visible = false;

                break;
            case ElectionPhase.NominationPhase:
                if(!user.CanVote)
                    break;
                
                if (CommitteeWTSNomination.FindCommitteeWTSNomination(session,
                    election.ID, user.ID).Count == 0)
                {
                    FacultyNomination.Visible = true;
                    BuildUserNominationOptions();
                }
                else
                    FacultyNominationComplete.Visible = true;
                break;
            case ElectionPhase.VotePhase:
                if(!user.CanVote)
                    break;
                
                if (BallotFlag.FindBallotFlag(session, election.ID, user.ID) ==
                    null)
                {
                    FacultyVote.Visible = true;
                    BuildUserVoteOptions();
                }
                else
                    FacultyVoteComplete.Visible = true;
                break;
            case ElectionPhase.ClosedPhase:
                if (!user.IsNEC && !user.IsAdmin)
                    FacultyClosed.Visible = true;
                break;
        }
        
        JulioButtonHider.Visible = user.IsAdmin;
        CancelElection.Visible = user.IsAdmin;

        if (user.IsNEC && election.Phase == ElectionPhase.CertificationPhase)
        {
            ActivateTab("CertificationPhase");
            NECCertificationPanel.Visible = true;
            BuildNECVoteTable();
            if(Certification.FindCertification(session, election.ID, user.ID) != null)
            {
                NECCertifyAgreement.Visible = false;
                CertifyCheckBox.Visible = false;
                CertifyButton.Visible = false;
                CertifyWarning.Visible = false;
                NECCertificationComplete.Visible = true;
            }
        }
        
        DaysLeftInPhase();
        JulioButton.Text = "Switch to Next Phase";
        switch(election.Phase) {
            case ElectionPhase.WTSPhase:
                PhaseLiteral.Text = "WTS Phase";
                break;
            case ElectionPhase.NominationPhase:
                PhaseLiteral.Text = "Nomination Phase";
                break;
            case ElectionPhase.VotePhase:
                PhaseLiteral.Text = "Voting Phase";
                break;
            case ElectionPhase.CertificationPhase:
                PhaseLiteral.Text = "Certification Phase";
                if(ElectionConflict.FindElectionConflicts(session, election.ID).Count == 0)
                    JulioButton.Text = "Close Election";
                break;
            case ElectionPhase.ConflictPhase:
                PhaseLiteral.Text = "Conflict Resolution Phase";
                JulioButton.Text = "Close Election";
                break;
            case ElectionPhase.ClosedPhase:
                PhaseLiteral.Text = "Closed";
                CancelElection.Visible = false;
                JulioButtonHider.Visible = false;
                break;
        }
        
        if(user.IsAdmin) {

            ActivateTab(election.Phase.ToString());

            JulioButton.Visible = true;
            JulioButtonSpacerText.Visible = true;
            if(election.Phase >= ElectionPhase.ClosedPhase)
                closed_tab.Visible = true;
            if (election.Phase == ElectionPhase.ConflictPhase)
            {
                List<ElectionConflict> conflicts = ElectionConflict.FindElectionConflicts(session, election.ID);
                foreach (ElectionConflict conflict in conflicts)
                {
                    DatabaseEntities.User conflictUser1 =
                        DatabaseEntities.User.FindUser(session, conflict.FirstUser);
                    if (conflict.Type == ConflictType.ElectedToMultipleCommittees)
                        BuildMultipleCommitteesConflictPanel(conflictUser1, conflict.ID);
                    if (conflict.Type == ConflictType.TooManyDeptMembers)
                    {
                        DatabaseEntities.User conflictUser2 =
                        DatabaseEntities.User.FindUser(session, conflict.SecUser);
                        BuildTooManyDeptConflictPanel(conflictUser1,
                            conflictUser2, conflictUser2.Department, conflict.ID);
                    }
                }
                JulioButton.Visible = conflicts.Count == 0;
                JulioButtonSpacerText.Visible = conflicts.Count == 0;
                if (conflicts.Count == 0)
                    AdminNoConflicts.Visible = true;
                else
                    DaysRemaining.Text = "The election cannot be closed while conflicts are present.";
                
                conflicts_tab.Visible = true;
            }
            
            if (election.Phase >= ElectionPhase.CertificationPhase)
            {
                int numberCertifications = Certification.FindCertifications(session, election.ID).Count;
                string req_certs = System.Configuration.ConfigurationManager.AppSettings["required_nec_certs"];
                int nec_certs = req_certs != null ? int.Parse(req_certs) : 3;
                
                AdminCertCount.Text = "There are currently " + numberCertifications.ToString();
                if (numberCertifications >= nec_certs) // TODO: Add a button to advance to the next phase.
                    AdminCertCount.Text += " certifications, which is enough to proceed to the next stage.";
                else
                    AdminCertCount.Text += " certification(s).  More NEC members must certify the results before proceeding.";
                certifications_tab.Visible = true;
                necprogressbar.Attributes.Add("style", "width: " + Math.Min(100, numberCertifications * (100 / nec_certs)).ToString() + "%");

                if(numberCertifications < nec_certs) {
                    HtmlGenericControl pretext = new HtmlGenericControl("span");
                    pretext.InnerText = certifications_tab_link.Text;
                    certifications_tab_link.Controls.Add(pretext);

                    HtmlGenericControl badge = new HtmlGenericControl("span");
                    badge.Attributes["class"] = "badge badge-info";
                    badge.Attributes["style"] = "margin-left: 0.5em;";
                    badge.InnerText = numberCertifications.ToString();
                    certifications_tab_link.Controls.Add(badge);
                }


            }
            if (election.Phase >= ElectionPhase.VotePhase)
            {
                votes_tab.Visible = true;
                BuildAdminVoteTable();
            }
            if (election.Phase >= ElectionPhase.NominationPhase)
            {
                nominations_tab.Visible = true;
                BuildAdminNominationTable();
            }
            if(election.Phase >= ElectionPhase.WTSPhase &&
               election.Phase < ElectionPhase.ClosedPhase)
                wts_tab.Visible = true;

            //*******************************
            //******** Admin WTS Load *******
            //*******************************

            List<DatabaseEntities.CommitteeWTS> wtsList = DatabaseEntities.CommitteeWTS.FindCommitteeWTS(session, election.ID);

            foreach (DatabaseEntities.CommitteeWTS wts in wtsList)
            {
                DatabaseEntities.User wtsUser = DatabaseEntities.User.FindUser(session, wts.User);
                if(wtsUser == null)
                    continue;

                TableRow tr = new TableRow();

                Label revokeNameLabel = new Label();
                revokeNameLabel.Text = wtsUser.FirstName + " " + wtsUser.LastName;
                TableCell td1 = new TableCell();
                td1.Controls.Add(revokeNameLabel);

                Label revokeDeptLabel = new Label();
                revokeDeptLabel.Text = wtsUser.Department.ToString();
                TableCell td2 = new TableCell();
                td2.Controls.Add(revokeDeptLabel);

                Button revokeButton = new Button();
                revokeButton.Text = "Revoke";
                revokeButton.CssClass = "btn btn-danger btn-small";
                revokeButton.CommandArgument = wts.User.ToString();
                revokeButton.Click += new System.EventHandler(this.wtsRevoke_Click);
                TableCell td3 = new TableCell();
                td3.Controls.Add(revokeButton);

                tr.Cells.Add(td1);
                tr.Cells.Add(td2);
                tr.Cells.Add(td3);

                wtsAdminTable.Rows.Add(tr);

            }
            if(wtsList.Count == 0) {
                TableRow tr = new TableRow();

                TableCell td1 = new TableCell();
                td1.Controls.Add(new LiteralControl("No WTS forms have been submitted yet."));
                td1.ColumnSpan = 3;
                tr.Controls.Add(td1);

                wtsAdminTable.Rows.Add(tr);
            }
        }

    }

    protected void Page_PreRender(object sender, EventArgs e)
    {
        if(user.IsAdmin)
            DeltaText.Text = num_days_remaining().ToString();
        
        if(user.IsAdmin && election.Phase != ElectionPhase.ClosedPhase) {
            phasedelta.Visible = true;
            JulioButtonPhase.SelectedValue = election.Phase.ToString();
        }
        
        IsSpecial.Visible = election.SpecialElection;
        specialelection.Visible = election.SpecialElection && election.Phase != ElectionPhase.ClosedPhase;
        if(election.SpecialElection)
            MakeElectionSpecial.Text = "Not Special";
        else
            MakeElectionSpecial.Text = "Make Special Election";
    }
    
    protected string GetName(int UserID) {
        User u = DatabaseEntities.User.FindUser(session, UserID);
        if(u == null)
            return "(deleted)";
        return u.FirstName + " " + u.LastName;
    }
    
    private int num_days_remaining() {
        return election.DaysRemainingInPhase(session);
    }
    
    private void DaysLeftInPhase()
    {
        DaysRemaining.Text = "The election is closed.";
        phasedeltaedit.Visible = false;
        if(election.Phase != ElectionPhase.ClosedPhase)
        {
            int days_remaining = num_days_remaining();
            if(days_remaining > 1000) { // Not sure what MAXDATE translate to as an integer...
                if(election.Phase == ElectionPhase.CertificationPhase)
                    DaysRemaining.Text = "The phase should not be changed until the election has been certified by NEC members.";
                else
                    DaysRemaining.Text = "The phase should not be changed until some actions have occurred.";
            } else if(days_remaining > 0) {
                DaysRemaining.Text = days_remaining.ToString() + " day(s) remaining for this phase.";
                phasedeltaedit.Visible = user.IsAdmin;
            } else {
                DaysRemaining.Text = "This phase is " + (days_remaining * -1 + 1).ToString() + " day(s) overdue.";
                phasedeltaedit.Visible = user.IsAdmin;
            }
        }
    }

    private void ActivateTab(string tab_name) {
        AdminTabs.Visible = true;
        closed_tab.Attributes["class"] = "";
        conflicts_tab.Attributes["class"] = "";
        certifications_tab.Attributes["class"] = "";
        votes_tab.Attributes["class"] = "";
        nominations_tab.Attributes["class"] = "";
        wts_tab.Attributes["class"] = "";

        AdminWTSPanel.Visible = false;
        AdminNominationsPanel.Visible = false;
        AdminVotingPanel.Visible = false;
        AdminCertificationPanel.Visible = false;
        AdminConflictPanel.Visible = false;
        AdminClosedPanel.Visible = false;

        switch(tab_name) {
            case "WTSPhase":
                wts_tab.Visible = true;
                wts_tab.Attributes["class"] = "active";
                AdminWTSPanel.Visible = true;
                break;
            case "NominationPhase":
                nominations_tab.Visible = true;
                nominations_tab.Attributes["class"] = "active";
                AdminNominationsPanel.Visible = true;
                break;
            case "VotePhase":
                votes_tab.Visible = true;
                votes_tab.Attributes["class"] = "active";
                AdminVotingPanel.Visible = true;
                break;
            case "CertificationPhase":
                certifications_tab.Visible = true;
                certifications_tab.Attributes["class"] = "active";
                AdminCertificationPanel.Visible = user.IsAdmin;
                break;
            case "ConflictPhase":
                conflicts_tab.Visible = true;
                conflicts_tab.Attributes["class"] = "active";
                AdminConflictPanel.Visible = true;
                break;
            case "ClosedPhase":
                closed_tab.Visible = true;
                closed_tab.Attributes["class"] = "active";
                AdminClosedPanel.Visible = true;
                break;
            default:
                throw new HttpException(500, "Unexpected election phase.");
        }
    }

    protected void JulioButton_Clicked(Object sender, EventArgs e)
    {
        ElectionPhase next_phase = election.NextPhase(session);
        election.SetPhase(session, next_phase);

        Response.Redirect("/committee_election.aspx?id=" + election.ID.ToString());
    }

    protected void JulioButtonCustom_Clicked(Object sender, EventArgs e)
    {
        ElectionPhase next_phase = (ElectionPhase)Enum.Parse(typeof(ElectionPhase), JulioButtonPhase.SelectedValue);
        election.SetPhase(session, next_phase);

        Response.Redirect("/committee_election.aspx?id=" + election.ID.ToString());
    }

    protected void Tab_Clicked(Object sender, EventArgs e)
    {
        ActivateTab(((LinkButton)sender).CommandName);
    }

    /// <summary>
    /// Build the admin / NEC nomination table: add names for each nominee,
    /// add their number of primary votes, and add a boolean value indicating
    /// whether or not they will go on to the election.
    /// </summary>
    private void BuildAdminNominationTable()
    {
        // Get users for each election
        List<User> users = DatabaseEntities.User.FindUsers(session, election.ID);
        Dictionary<int, int> nomCount = new Dictionary<int, int>();

        // Count nominations for each user.
        foreach (DatabaseEntities.User aUser in users)
            nomCount.Add(aUser.ID, 0);

        List<CommitteeWTSNomination> nominations =
            CommitteeWTSNomination.FindCommitteeWTSNominations(session, ElectionID);
        foreach (CommitteeWTSNomination nom in nominations)
            nomCount[nom.Candidate]++;

        // Pull a list of the nominees according to the current votes.
        List<User> nominees = election.GetNominees(session);

        foreach (DatabaseEntities.User user in users)
        {
            TableRow row = new TableRow();
            TableCell name, votes, candidate;

            name = new TableCell();
            name.Controls.Add(
                new LiteralControl(user.FirstName + " " + user.LastName));
            row.Cells.Add(name);

            votes = new TableCell();
            votes.Controls.Add(
                new LiteralControl(nomCount[user.ID].ToString()));
            row.Cells.Add(votes);

            candidate = new TableCell();
            candidate.Controls.Add(
                new LiteralControl(nominees.Contains(user) ? "Yes" : "No"));
            row.Cells.Add(candidate);

            AdminNominationsTable.Rows.Add(row);
        }
    }

    /// <summary>
    /// Build the admin vote table: add cells for the nominee's names, and their
    /// current number of votes.
    /// </summary>
    private void BuildAdminVoteTable()
    {
        // Get nominees for the election.
        List<User> users = election.GetNominees(session);
        Dictionary<int, int> voteCount = new Dictionary<int, int>();

        // Count votes for each user.
        foreach (DatabaseEntities.User aUser in users)
        {
            voteCount.Add(aUser.ID, 0);
        }
        List<BallotEntry> entries = BallotEntry.FindBallotEntry(session, election.ID);
        foreach (BallotEntry entry in entries)
            voteCount[entry.Candidate]++;


        foreach (DatabaseEntities.User user in users)
        {
            TableRow row = new TableRow();
            TableCell name, votes;

            name = new TableCell();
            name.Controls.Add(
                new LiteralControl(user.FirstName + " " + user.LastName));
            row.Cells.Add(name);

            votes = new TableCell();
            votes.Controls.Add(
                new LiteralControl(voteCount[user.ID].ToString()));
            row.Cells.Add(votes);

            AdminVotingTable.Rows.Add(row);
        }

        if(users.Count == 0) {
            TableRow tr = new TableRow();

            TableCell td1 = new TableCell();
            td1.Controls.Add(new LiteralControl("No ballots have been cast yet."));
            td1.ColumnSpan = 3;
            tr.Controls.Add(td1);

            AdminVotingTable.Rows.Add(tr);
        }
    }

    private void BuildNECVoteTable()
    {
        // Get nominees for the election.
        List<User> users = election.GetNominees(session);
        Dictionary<int, int> voteCount = new Dictionary<int, int>();

        // Count votes for each user.
        foreach (DatabaseEntities.User aUser in users)
        {
            voteCount.Add(aUser.ID, 0);
        }
        List<BallotEntry> entries = BallotEntry.FindBallotEntry(session, election.ID);
        foreach (BallotEntry entry in entries)
            voteCount[entry.Candidate]++;


        foreach (DatabaseEntities.User user in users)
        {
            TableRow row = new TableRow();
            TableCell name, votes;

            name = new TableCell();
            name.Controls.Add(
                new LiteralControl(user.FirstName + " " + user.LastName));
            row.Cells.Add(name);

            votes = new TableCell();
            votes.Controls.Add(
                new LiteralControl(voteCount[user.ID].ToString()));
            row.Cells.Add(votes);

            NECVotingTable.Rows.Add(row);
        }
    }

    /// <summary>
    /// Build the radio button list which lists all the nominees a user can vote
    /// for in a primary election.
    /// </summary>
    private void BuildUserNominationOptions()
    {
        if(Page.IsPostBack)
            return;
        
        ListViewNom.DataSource = election.Nominees(session);
        ListViewNom.DataBind();
    }

    /// <summary>
    /// Build the radio button list which allows users to select a nominee.
    /// </summary>
    private void BuildUserVoteOptions()
    {
        if(Page.IsPostBack)
            return;

        // Get nominees for this election.
        List<User> nominees = election.GetNominees(session);
        
        ListViewVote.DataSource = election.Nominees(session);
        ListViewVote.DataBind();
    }

    /// <summary>
    /// Onclick, cast the user's vote based off of the currently selected radiobutton.
    /// </summary>
    protected void FacultyCastNomination_Click(Object sender, EventArgs e)
    {
        // begin our transaction.
        ITransaction transaction = session.BeginTransaction();

        // Get the identity of the voting user
        User user = DatabaseEntities.User.FindUser(session, User.Identity.Name);


        // Add the WTSNomination if this user hasn't already cast one.
        if (CommitteeWTSNomination.FindCommitteeWTSNomination(session, election.ID, user.ID).Count == 0)
        {
            int count = 0;
            foreach (ListViewDataItem eachItem in ListViewNom.Items) {
                CheckBox entry = (CheckBox)eachItem.FindControl("PrimBallotEntry");
                if(!entry.Checked)
                    continue;
                count++;
            }
            if(count > election.VacanciesToFill) {
                TooManyPrimVotes.Visible = true;
                NumVacancies_Prim1.Text = election.VacanciesToFill.ToString();
                return;
            }
            TooManyPrimVotes.Visible = false;
            foreach (ListViewDataItem eachItem in ListViewNom.Items) {
                CheckBox entry = (CheckBox)eachItem.FindControl("PrimBallotEntry");
                if(!entry.Checked)
                    continue;
                
                HiddenField candidate = (HiddenField)eachItem.FindControl("WTS_Candidate");
                
                CommitteeWTSNomination nomination = new CommitteeWTSNomination();
                nomination.Election = election.ID;
                nomination.Voter = user.ID;
                nomination.Candidate = int.Parse(candidate.Value);
                session.SaveOrUpdate(nomination);
                
            }
            session.Flush();
            FacultyNomination.Visible = false;
            FacultyNominationComplete.Visible = true;
        }
        else
            ; // We should never have to deal with this, though we can add error checking if need-be.
        NHibernateHelper.Finished(transaction);
    }

    protected void GeneratePDFButton_Click(Object sender, EventArgs e)
    {
        ISession session = NHibernateHelper.CreateSessionFactory().OpenSession();

        election.GenerateResultsPDF(session, Server.MapPath("CertificationForms\\form.pdf"));
        Response.Redirect("/CertificationForms/form.pdf");
    }

    /// <summary>
    /// OnClick, cast the user's vote based off of the currently selected radiobutton.
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    protected void FacultyCastVote_Click(Object sender, EventArgs e)
    {
        // No defined error because this should never happen IRL.
        if(ListViewVote.Items.Count == 0)
            return;
        
        // open the session
        ITransaction transaction = session.BeginTransaction();

        // get the user who has the page open
        User user = DatabaseEntities.User.FindUser(session, User.Identity.Name);


        // store the new flag and entry if there is no flag pertaining to this user
        // in this election
        if (BallotFlag.FindBallotFlag(session, election.ID, user.ID) == null)
        {
            int count = 0;
            foreach (ListViewDataItem eachItem in ListViewVote.Items) {
                CheckBox entry = (CheckBox)eachItem.FindControl("GenBallotEntry");
                if(!entry.Checked)
                    continue;
                count++;
            }
            if(count > election.VacanciesToFill) {
                TooManyGenVotes.Visible = true;
                NumVacancies_Gen1.Text = election.VacanciesToFill.ToString();
                return;
            }
            TooManyGenVotes.Visible = false;
            
            foreach(ListViewItem lvi in ListViewVote.Items) {
                CheckBox cbentry = (CheckBox)lvi.FindControl("GenBallotEntry");
                if(!cbentry.Checked)
                    continue;
                // form the ballot entry
                BallotEntry entry = new BallotEntry();
                entry.Election = election.ID;
                entry.Candidate = int.Parse(((HiddenField)lvi.FindControl("WTS_Candidate")).Value);
                session.SaveOrUpdate(entry);
            }

            // form the ballot flag
            BallotFlag flag = new BallotFlag();
            flag.Election = election.ID;
            flag.User = user.ID;
            session.SaveOrUpdate(flag);
            
            session.Flush();

            FacultyVote.Visible = false;
            FacultyVoteComplete.Visible = true;
        }

        NHibernateHelper.Finished(transaction);
    }

    protected void DeltaSubmit_Click(Object sender, EventArgs e)
    {
        ITransaction transaction = session.BeginTransaction();
        election.PhaseEndDelta = int.Parse(DeltaText.Text) - election.RealDaysRemainingInPhase(session);
        session.SaveOrUpdate(election);
        session.Flush();
        NHibernateHelper.Finished(transaction);

        Response.Redirect("/committee_election.aspx?id=" + election.ID.ToString());
    }

    protected void MakeElectionSpecial_Click(Object sender, EventArgs e)
    {
        ITransaction transaction = session.BeginTransaction();
        election.SpecialElection = election.SpecialElection ? false : true;
        session.SaveOrUpdate(election);
        session.Flush();
        NHibernateHelper.Finished(transaction);
    }
    
    protected void CancelElection_Click(Object sender, EventArgs e)
    {
        ITransaction transaction = session.BeginTransaction();
        election.DestroyElection(session, transaction);
        NHibernateHelper.Finished(transaction);
        
        Response.Redirect("/home.aspx");
    }

    /// <summary>
    /// This function builds a panel with the controls necessary to
    /// allow the admin to perform shared-department-conflict resolution.
    /// </summary>
    /// <param name="user1">The first user involved in the conflict.</param>
    /// <param name="user2">The second user involved in the conflict.</param>
    /// <param name="department">The department the two users share.</param>
    private void BuildTooManyDeptConflictPanel(User user1, User user2,
        DepartmentType department, int conflictID)
    {
        // create the panel
        Panel panel = new Panel();
        panel.CssClass = "alert";
        panel.ID = "ConflictPanel" + conflictID.ToString("0000");

        // Create a label which explicates the conflict
        HtmlGenericControl message = new HtmlGenericControl("p");
        message.InnerText = user1.FirstName + " " + user1.LastName + " and "
            + user2.FirstName + " " + user2.LastName + " were both elected to the "
            + committee.Name + "but they are both members of the " + user1.Department.ToString()
            + " department.  Only one member of each department may be elected to the committee.";
        panel.Controls.Add(message);

        HtmlGenericControl hp = new HtmlGenericControl("p");
        // add a button which will allow the admin to disqualify the
        // first person in the conflict
        Button first = new Button();
        first.CssClass = "btn btn-warning";
        first.ID = user1.Email + conflictID.ToString("0000");
        first.Text = "Disqualify " + user1.FirstName + " " + user1.LastName;
        first.Click += new EventHandler(this.Disq_Click);
        hp.Controls.Add(first);

        // add a button which will allow the admin to disqualify the
        // second person in the conflict
        Button second = new Button();
        second.CssClass = "btn btn-warning";
        second.ID = user2.Email + conflictID.ToString("0000");
        second.Text = "Disqualify " + user2.FirstName + " " + user2.LastName;
        second.Click += new EventHandler(this.Disq_Click);
        hp.Controls.Add(second);
        
        panel.Controls.Add(hp);

        AdminConflictPanel.Controls.Add(panel);
    }

    protected void Certify_Click(Object sender, EventArgs e)
    {
        ITransaction transaction = session.BeginTransaction();
        user = DatabaseEntities.User.FindUser(session, User.Identity.Name);
        // If the confirmation box is ticked, submit the certification
        if (CertifyCheckBox.Checked == true)
        {
            Certification certification = new Certification();
            certification.Election = election.ID;
            certification.User = user.ID;
            session.SaveOrUpdate(certification);
            session.Flush();
            CertifyCheckBox.Visible = false;
            CertifyButton.Visible = false;
            CertifyWarning.Visible = false;
            NECCertifyAgreement.Visible = false;
            NECCertificationComplete.Visible = true;
        }
        else // otherwise display the error label
            CertifyWarning.Visible = true;

        NHibernateHelper.Finished(transaction);
    }

    /// <summary>
    /// This function creates a panel with the controls necessary to allow the admin
    /// to perform multiple committee conflict resolution.
    /// </summary>
    /// <param name="user">The user involved in the conflict.</param>
    private void BuildMultipleCommitteesConflictPanel(User user, int conflictID)
    {
        // create the panel
        Panel panel = new Panel();
        panel.CssClass = "alert";
        panel.ID = "ConflictPanel" + conflictID.ToString("0000");

        // Create a label which explicates the conflict
        HtmlGenericControl message = new HtmlGenericControl("p");
        message.InnerText = user.FirstName + " " + user.LastName + " was elected to the "
            + committee.Name;
        message.InnerText +=
            (user.CurrentCommittee != DatabaseEntities.User.NoCommittee) ?
            (" but he or she currently serves on the " + Committee.FindCommittee(session, user.CurrentCommittee).Name) :
            (" but he or she is currently serving as the " + user.OfficerPosition.ToString());
        message.InnerText += ". This member may only hold one position at a time.";
        panel.Controls.Add(message);

        HtmlGenericControl hp = new HtmlGenericControl("p");
        
        // add a button which will allow the admin to disqualify the
        // first person in the conflict
        Button first = new Button();
        first.CssClass = "btn btn-warning";
        first.ID = user.Email + conflictID.ToString("0000");
        first.Text = "Disqualify " + user.FirstName + " " + user.LastName;
        first.Click += new EventHandler(this.Disq_Click);
        hp.Controls.Add(first);

        // add a button which will allow the admin to disqualify the
        // second person in the conflict
        Button second = new Button();
        second.CssClass = "btn";
        second.ID = "Ignore" + conflictID.ToString("0000");
        second.Text = "Ignore conflict";
        second.Click += new EventHandler(this.Ignore_Click);
        hp.Controls.Add(second);
        
        panel.Controls.Add(hp);

        AdminConflictPanel.Controls.Add(panel);
    }

    /// <summary>
    ///  The even handler for when the admin clicks on a disqualification button.
    /// </summary>
    protected void Disq_Click(Object sender, EventArgs e)
    {
        ITransaction transaction = session.BeginTransaction();

        Button sendButton = (Button)sender;
        string toDisqualify = sendButton.ID.Substring(0, sendButton.ID.Length - 4);
        DatabaseEntities.User user = DatabaseEntities.User.FindUser(session, toDisqualify);

        // Disqualify the user by revoking their WTS
        election.RevokeWTS(session, transaction, user.ID);

        // remove the panel that represented this now resolved conflict
        string idToFind = sendButton.ID.Substring(sendButton.ID.Length - 4, 4);

        for (int i = 0; i < AdminConflictPanel.Controls.Count; i++)
        {
            if (AdminConflictPanel.Controls[i].ID == "ConflictPanel" + idToFind)
            {
                AdminConflictPanel.Controls.RemoveAt(i);
                break;
            }
        }

        // remove the election conflict
        int id = int.Parse(idToFind);

        ElectionConflict conflict = ElectionConflict.FindElectionConflict(session,
            id);
        NHibernateHelper.Delete(session, conflict);

        // Check if we should display there are no more conflicts.
        if (ElectionConflict.FindElectionConflicts(session, election.ID).Count == 0)
            AdminNoConflicts.Visible = true;

        NHibernateHelper.Finished(transaction);
    }

    /// <summary>
    ///  The even handler for when the admin clicks on an ignore conflict button.
    /// </summary>
    protected void Ignore_Click(Object sender, EventArgs e)
    {
        Button sendButton = (Button)sender;

        // remove the panel that represented this now resolved conflict and delete
        // the election conflict
        string idToFind = sendButton.ID.Substring(sendButton.ID.Length - 4, 4);

        // remove the election conflict
        int id = int.Parse(idToFind);

        ElectionConflict conflict = ElectionConflict.FindElectionConflict(session, id);
        NHibernateHelper.Delete(session, conflict);

        // remove the programmatically generated panel
        for (int i = 0; i < AdminConflictPanel.Controls.Count; i++)
        {
            if (AdminConflictPanel.Controls[i].ID == "ConflictPanel" + idToFind)
            {
                AdminConflictPanel.Controls.RemoveAt(i);
                break;
            }
        }


        // Check if we should display there are no more conflicts.
        if (ElectionConflict.FindElectionConflicts(session, election.ID).Count == 0)
            AdminNoConflicts.Visible = true;
    }

    protected void wtsSubmit_Click(object sender, EventArgs e)
    {
        Page.Validate("wts");
        if (!Page.IsValid)
            return;
        
        if(wtsStatement.Text.Length > 1000) {
            wtsPanelLength.Visible = true;
            return;
        }
        
        // Check to make sure that the user hasn't already submitted a WTS.
        List<DatabaseEntities.CommitteeWTS> wtsList = DatabaseEntities.CommitteeWTS.FindCommitteeWTS(session, election.ID);
        foreach (DatabaseEntities.CommitteeWTS wts in wtsList)
        {
            if (wts.Election == election.ID && wts.User == user.ID &&
                (!committee.TenureRequired || user.IsTenured) &&
                (!committee.BargainingUnitRequired || user.IsBargainingUnit))
                return;
        }
        
        ITransaction transaction = session.BeginTransaction();

        DatabaseEntities.CommitteeElection.WillingToServe(session, user.ID, election.ID, wtsStatement.Text);
        DatabaseEntities.NHibernateHelper.Finished(transaction);

        wtsPanelLength.Visible = false;
        wtsPanelNew.Visible = false;
        wtsPanelDone.Visible = true;

    }

    protected void wtsAcceptValidator_ServerValidate(object source, ServerValidateEventArgs args)
    {
        args.IsValid = wtsConfirm.Checked;
    }

    protected void wtsRevoke_Click(object sender, EventArgs e)
    {
        int id = int.Parse(((Button)sender).CommandArgument);

        ITransaction transaction = session.BeginTransaction();

        List<DatabaseEntities.CommitteeWTS> wtsList = DatabaseEntities.CommitteeWTS.FindCommitteeWTS(session, election.ID);
        election.RevokeWTS(session, transaction, id);

        DatabaseEntities.NHibernateHelper.Finished(transaction);
        wtsAdminConfirm.Visible = true;
        
    }
}

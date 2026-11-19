using System.Collections.Generic;
using DeadSpin.Core.Campaign;
using DeadSpin.Core.Economy;
using DeadSpin.Core.GameState;
using DeadSpin.Core.Progression;
using DeadSpin.Core.Specials;
using UnityEngine;

namespace DeadSpin.Runtime.UI
{
    /// <summary>
    /// The casino's other rooms, opened as panels from the seat (the player never leaves the table),
    /// plus the offers that interrupt play: challenges, contracts, the auction and His Final Offer.
    /// </summary>
    public sealed partial class GameHud
    {
        private static readonly RoomId[] Rooms = { RoomId.PrivateLounge, RoomId.Shop, RoomId.Vault, RoomId.RecordsRoom, RoomId.Office };

        private RoomId? _room;
        private string _roomText;
        private string _recordShown;
        private Vector2 _roomScroll;
        private readonly Dictionary<int, string> _bids = new Dictionary<int, string>();
        private string _vaultAmount = "1000";

        private static string RoomName(RoomId room)
        {
            switch (room)
            {
                case RoomId.PrivateLounge: return "Lounge";
                case RoomId.Shop: return "Shop";
                case RoomId.Vault: return "Vault";
                case RoomId.RecordsRoom: return "Records";
                case RoomId.Office: return "Office";
                default: return "Table";
            }
        }

        private void DrawRoomButtons(CasinoRun casino)
        {
            var rect = new Rect(24f, 350f, 340f, 84f);
            Panel(rect);
            GUILayout.BeginArea(Inset(rect, 10f));
            GUILayout.Label("ROOMS", _skin.Label);
            GUILayout.BeginHorizontal();
            foreach (RoomId room in Rooms)
            {
                string label = RoomName(room);
                if (casino.GameMasterSighting == room) label += "*";
                bool open = _room == room;
                if (GUILayout.Button(label, open ? _skin.TabActive : _skin.Tab, GUILayout.Height(30f)))
                {
                    if (open) CloseRoom();
                    else OpenRoom(casino, room);
                }
            }
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        private void OpenRoom(CasinoRun casino, RoomId room)
        {
            _room = room;
            _recordShown = null;
            _roomScroll = Vector2.zero;
            switch (room)
            {
                case RoomId.PrivateLounge: _roomText = CasinoRecords.StaffLine(casino.Stage, Random.Range(0, 8)); break;
                case RoomId.Office: _roomText = casino.VisitOffice(); break;
                default: _roomText = null; break;
            }
        }

        private void CloseRoom()
        {
            _room = null;
            _roomText = null;
            _recordShown = null;
        }

        private void DrawRoom(CasinoRun casino)
        {
            if (_room == null) return;
            RoomId room = _room.Value;
            var rect = new Rect(380f, 140f, 620f, Mathf.Min(680f, _height - 340f));
            Panel(rect);
            GUILayout.BeginArea(Inset(rect));
            GUILayout.BeginHorizontal();
            GUILayout.Label(RoomTitle(room), _skin.Title);
            if (GUILayout.Button("Close", _skin.Tab, GUILayout.Width(90f), GUILayout.Height(30f))) { CloseRoom(); GUILayout.EndHorizontal(); GUILayout.EndArea(); return; }
            GUILayout.EndHorizontal();
            if (casino.GameMasterSighting == room)
            {
                _skin.Small.normal.textColor = HudSkin.Blood;
                GUILayout.Label("He is here. He is not supposed to be here. He watches you without blinking.", _skin.Small);
                _skin.Small.normal.textColor = HudSkin.Bone;
            }
            _roomScroll = GUILayout.BeginScrollView(_roomScroll);
            switch (room)
            {
                case RoomId.PrivateLounge: DrawLounge(casino); break;
                case RoomId.Shop: DrawShop(casino); break;
                case RoomId.Vault: DrawVault(casino); break;
                case RoomId.RecordsRoom: DrawRecords(casino); break;
                case RoomId.Office: DrawOffice(casino); break;
            }
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private static string RoomTitle(RoomId room)
        {
            switch (room)
            {
                case RoomId.PrivateLounge: return "The Private Lounge";
                case RoomId.Shop: return "The House Shop";
                case RoomId.Vault: return "The Vault";
                case RoomId.RecordsRoom: return "The Records Room";
                case RoomId.Office: return "The Game Master's Office";
                default: return "";
            }
        }

        private bool BetweenRounds(CasinoRun casino)
        {
            RoundPhase phase = casino.Run.Round.Phase;
            return phase == RoundPhase.Idle || phase == RoundPhase.Summary;
        }

        // ------------------------------------------------------------------ lounge

        private void DrawLounge(CasinoRun casino)
        {
            if (!string.IsNullOrEmpty(_roomText)) GUILayout.Label(_roomText, _skin.Small);
            GUILayout.Space(10f);
            bool canRest = casino.Records.CanRest(casino.Stage) && BetweenRounds(casino);
            GUI.enabled = canRest;
            if (GUILayout.Button(canRest ? "Sit a while (+1 Influence, once per stage)" : "You have already rested this stage", _skin.Button, GUILayout.Height(40f)))
            {
                _roomText = casino.Rest();
            }
            GUI.enabled = true;
            if (!BetweenRounds(casino)) GUILayout.Label("You can only rest between rounds.", _skin.Small);
        }

        // ------------------------------------------------------------------ shop

        private void DrawShop(CasinoRun casino)
        {
            GUILayout.Label("Prices rise as the night goes on. Everything is paid from your balance.", _skin.Small);
            if (!string.IsNullOrEmpty(_roomText)) { _skin.Small.normal.textColor = HudSkin.Gold; GUILayout.Label(_roomText, _skin.Small); _skin.Small.normal.textColor = HudSkin.Bone; }
            foreach (ShopOffer offer in casino.Shop.Shelf)
            {
                GUILayout.Space(6f);
                GUILayout.BeginHorizontal();
                GUILayout.BeginVertical();
                GUILayout.Label(offer.Item.Name + (offer.Remaining > 1 ? "  (" + offer.Remaining + " left)" : ""), _skin.Label);
                GUILayout.Label(offer.Item.Description, _skin.Small);
                GUILayout.EndVertical();
                GUI.enabled = offer.Remaining > 0 && casino.Ledger.Balance >= offer.Price;
                if (GUILayout.Button(offer.Remaining > 0 ? Money(offer.Price) : "Sold", _skin.Button, GUILayout.Width(130f), GUILayout.Height(44f)))
                {
                    string error = casino.BuyItem(offer.Item.Id);
                    _roomText = error ?? "Bought: " + offer.Item.Name + ".";
                }
                GUI.enabled = true;
                GUILayout.EndHorizontal();
            }
        }

        // ------------------------------------------------------------------ vault

        private void DrawVault(CasinoRun casino)
        {
            Row("BALANCE", Money(casino.Ledger.Balance), HudSkin.Bone);
            Row("STORED", Money(casino.Ledger.Stored), HudSkin.Bone);
            Row("DEBT", Money(casino.Ledger.Debt), HudSkin.Blood);
            GUILayout.Label("Stored money cannot be bet, taxed or taken by the table. It still counts toward bankruptcy and some endings.", _skin.Small);

            foreach (Installment installment in casino.Payments.Installments)
            {
                if (installment.CumulativeAmount <= 0) continue;
                string state = installment.Settled ? "paid" : installment.Missed ? "MISSED" : "due after round " + installment.DueRound;
                GUILayout.Label("Stage " + installment.Stage + " installment: " + Money(installment.CumulativeAmount) + " paid in total by then (" + state + ")", _skin.Small);
            }
            Installment next = casino.Payments.Next;
            if (next != null) GUILayout.Label("Still owed for the next installment: " + Money(casino.Payments.OutstandingFor(next)), _skin.Label);

            GUILayout.Space(8f);
            GUILayout.BeginHorizontal();
            GUILayout.Label("AMOUNT  $", _skin.Label, GUILayout.Width(110f));
            _vaultAmount = GUILayout.TextField(_vaultAmount, 12, GUILayout.Width(140f), GUILayout.Height(30f));
            GUILayout.EndHorizontal();
            long amount;
            if (!long.TryParse(_vaultAmount.Replace(",", "").Replace("$", ""), out amount) || amount < 0) amount = 0;

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Pay debt", _skin.Button, GUILayout.Height(38f))) _roomText = Report("Paid", casino.PayDebt(amount));
            if (GUILayout.Button("Pay all I hold", _skin.Button, GUILayout.Height(38f))) _roomText = Report("Paid", casino.PayDebt(casino.Ledger.Balance));
            if (GUILayout.Button("Pay from vault", _skin.Button, GUILayout.Height(38f))) _roomText = Report("Paid from the vault", casino.PayDebt(amount, true));
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Deposit", _skin.Button, GUILayout.Height(38f))) _roomText = Report("Deposited", casino.Deposit(amount));
            if (GUILayout.Button("Withdraw", _skin.Button, GUILayout.Height(38f))) _roomText = Report("Withdrew", casino.Withdraw(amount));
            if (GUILayout.Button("Withdraw all", _skin.Button, GUILayout.Height(38f))) _roomText = Report("Withdrew", casino.Withdraw(casino.Ledger.Stored));
            GUILayout.EndHorizontal();
            GUILayout.Label("Payments can be made before the spin; deposits and withdrawals only between rounds.", _skin.Small);
            if (!string.IsNullOrEmpty(_roomText)) GUILayout.Label(_roomText, _skin.Label);
        }

        private static string Report(string verb, long amount)
        {
            return amount > 0 ? verb + " " + Money(amount) + "." : "Nothing happened. (Check the timing and your funds.)";
        }

        // ------------------------------------------------------------------ records

        private void DrawRecords(CasinoRun casino)
        {
            GUILayout.Label("Dusty files, ledgers and letters. Secrets earn Influence and count as evidence.  Evidence: " + casino.LoopholeEvidence, _skin.Small);
            if (_recordShown != null)
            {
                RecordEntry entry = casino.Records.Get(_recordShown);
                GUILayout.Space(6f);
                GUILayout.Label(entry.Title, _skin.Label);
                GUILayout.Label(_roomText ?? entry.Text, _skin.Small);
                if (GUILayout.Button("Back to the shelves", _skin.Tab, GUILayout.Height(30f))) { _recordShown = null; _roomText = null; }
                return;
            }
            foreach (RecordEntry entry in casino.Records.Entries)
            {
                bool readable = casino.Records.CanRead(entry, casino.Stage, casino.Profile.TotalLoopholeExploits);
                string label;
                if (!readable) label = entry.MinStage > casino.Stage ? "(locked until later tonight)" : "(sealed: find more loopholes)";
                else label = entry.Title + (casino.Records.HasRead(entry.Id) ? "" : "   NEW") + (entry.IsSecret && casino.Records.HasRead(entry.Id) ? "   [secret]" : "");
                GUI.enabled = readable;
                if (GUILayout.Button(label, _skin.Tab, GUILayout.Height(30f)))
                {
                    _recordShown = entry.Id;
                    _roomText = casino.ReadRecord(entry.Id);
                }
                GUI.enabled = true;
            }
        }

        // ------------------------------------------------------------------ office

        private void DrawOffice(CasinoRun casino)
        {
            if (!string.IsNullOrEmpty(_roomText)) GUILayout.Label(_roomText, _skin.Small);
            GUILayout.Space(8f);
            GUILayout.Label("CONTRACTS ON THE DESK", _skin.Label);
            if (!casino.CurrentStage.ContractsAvailable) { GUILayout.Label("The drawer is locked. Come back later in the night.", _skin.Small); return; }
            List<ContractDefinition> available = casino.AvailableContracts();
            if (available.Count == 0) GUILayout.Label("Nothing he is willing to sign right now.", _skin.Small);
            foreach (ContractDefinition contract in available)
            {
                GUILayout.Space(4f);
                GUILayout.Label(contract.Name, _skin.Label);
                GUILayout.Label(contract.Terms(casino), _skin.Small);
                GUI.enabled = BetweenRounds(casino);
                if (GUILayout.Button("Sign", _skin.Button, GUILayout.Width(120f), GUILayout.Height(34f)))
                {
                    string error = casino.SignContract(contract.Id);
                    _roomText = error ?? "Signed: " + contract.Name + ". The ink is still warm.";
                }
                GUI.enabled = true;
            }
            if (!BetweenRounds(casino)) GUILayout.Label("Contracts are signed between rounds.", _skin.Small);
        }

        // ------------------------------------------------------------------ offers and events

        private void DrawOffers(CasinoRun casino)
        {
            float x = _width - 424f;
            float y = Mathf.Max(_rulesBottom + 10f, 330f);
            const float w = 400f;

            ChallengeDefinition challenge = casino.Challenges.Offer;
            if (challenge != null && casino.Challenges.Active == null)
            {
                string text = challenge.Describe(casino) + "\nReward: " + challenge.RewardAt(casino.Stage).Describe();
                float h = _skin.Small.CalcHeight(new GUIContent(text), w - 28f) + 96f;
                var rect = new Rect(x, y, w, h);
                Panel(rect);
                GUILayout.BeginArea(Inset(rect));
                GUILayout.Label("CHALLENGE: " + challenge.Name.ToUpperInvariant(), _skin.Label);
                GUILayout.Label(text, _skin.Small);
                GUILayout.BeginHorizontal();
                GUI.enabled = BetweenRounds(casino);
                if (GUILayout.Button("Accept", _skin.Button, GUILayout.Height(34f))) casino.AcceptChallenge();
                GUI.enabled = true;
                if (GUILayout.Button("Decline", _skin.Button, GUILayout.Height(34f))) casino.DeclineChallenge();
                GUILayout.EndHorizontal();
                GUILayout.EndArea();
                y = rect.yMax + 8f;
            }

            ContractDefinition offer = casino.Contracts.Offer;
            if (offer != null && casino.PendingDecision == PendingDecision.None)
            {
                string text = offer.Terms(casino);
                float h = _skin.Small.CalcHeight(new GUIContent(text), w - 28f) + 96f;
                var rect = new Rect(x, y, w, h);
                Panel(rect);
                GUILayout.BeginArea(Inset(rect));
                GUILayout.Label("HE SLIDES A CONTRACT ACROSS: " + offer.Name.ToUpperInvariant(), _skin.Label);
                GUILayout.Label(text, _skin.Small);
                GUILayout.BeginHorizontal();
                GUI.enabled = BetweenRounds(casino);
                if (GUILayout.Button("Sign", _skin.Button, GUILayout.Height(34f)))
                {
                    string error = casino.SignContract(offer.Id);
                    if (error != null && table != null) table.ShowNotice(error);
                }
                GUI.enabled = true;
                if (GUILayout.Button("Refuse", _skin.Button, GUILayout.Height(34f))) casino.RefuseContractOffer();
                GUILayout.EndHorizontal();
                GUILayout.EndArea();
                y = rect.yMax + 8f;
            }

            AuctionState auction = casino.Events.Auction;
            if (auction != null && auction.Open)
            {
                float h = 60f + auction.Lots.Count * 86f;
                var rect = new Rect(x, y, w, h);
                Panel(rect);
                GUILayout.BeginArea(Inset(rect));
                GUILayout.Label("THE AUCTION (one sealed bid per lot)", _skin.Label);
                for (int i = 0; i < auction.Lots.Count; i++)
                {
                    AuctionLot lot = auction.Lots[i];
                    GUILayout.Label(lot.Name + ": " + lot.Description, _skin.Small);
                    GUILayout.BeginHorizontal();
                    if (lot.Closed)
                    {
                        GUILayout.Label(lot.Won ? "WON" : "Lost", _skin.Label);
                    }
                    else
                    {
                        string bid;
                        if (!_bids.TryGetValue(i, out bid)) bid = "500";
                        GUILayout.Label("$", _skin.Label, GUILayout.Width(14f));
                        _bids[i] = GUILayout.TextField(bid, 9, GUILayout.Width(110f), GUILayout.Height(28f));
                        long amount;
                        if (GUILayout.Button("Bid", _skin.Button, GUILayout.Width(80f), GUILayout.Height(28f)) && long.TryParse(_bids[i], out amount))
                        {
                            string result = casino.Bid(i, amount);
                            if (table != null) table.ShowNotice(result);
                        }
                    }
                    GUILayout.EndHorizontal();
                }
                GUILayout.EndArea();
            }
        }

        // ------------------------------------------------------------------ His Final Offer

        private void DrawFinalOffer(CasinoRun casino)
        {
            HudSkin.DrawSolid(new Rect(0f, 0f, _width, _height), new Color(0f, 0f, 0f, 0.6f));
            var rect = new Rect(_width * 0.5f - 420f, _height * 0.5f - 280f, 840f, 560f);
            Panel(rect);
            GUILayout.BeginArea(Inset(rect, 28f));
            GUILayout.Label("HIS FINAL OFFER", _skin.Title);
            GUILayout.Label("The music stops. He folds his hands and leans across the table. \"One spin left. Before it, a choice.\"", _skin.Small);
            GUILayout.Space(10f);

            ContractDefinition deal = ContractCatalog.Get("final_offer");
            ContractDefinition escape = ContractCatalog.Get("escape_clause");
            OfferButton("The Deal", deal != null ? deal.Terms(casino) : "", () => casino.AnswerFinalOffer(FinalOfferChoice.TheDeal));
            OfferButton("The Escape Clause", escape != null ? escape.Terms(casino) : "", () => casino.AnswerFinalOffer(FinalOfferChoice.EscapeClause));
            OfferButton("Refuse", "Play the last spin on your own terms. Whatever you hold when it stops is what you leave with.", () => casino.AnswerFinalOffer(FinalOfferChoice.Refuse));
            GUILayout.EndArea();
        }

        private void OfferButton(string title, string terms, System.Action choose)
        {
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(title, _skin.Button, GUILayout.Width(220f), GUILayout.Height(64f))) choose();
            GUILayout.Label(terms, _skin.Small);
            GUILayout.EndHorizontal();
            GUILayout.Space(10f);
        }
    }
}

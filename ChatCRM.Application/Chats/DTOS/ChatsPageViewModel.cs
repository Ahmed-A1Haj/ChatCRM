namespace ChatCRM.Application.Chats.DTOs
{
    public class ChatsPageViewModel
    {
        public List<InstanceDto> Instances { get; set; } = new();
        public int? ActiveInstanceId { get; set; }
        public List<ConversationDto> Conversations { get; set; } = new();
        public string Filter { get; set; } = "all";    // all, mine, unassigned, closed
        public byte? ActiveChannelType { get; set; }   // null = all platforms
        public List<TeamMemberDto> TeamMembers { get; set; } = new();
        public string? CurrentUserId { get; set; }
        public int CountAll { get; set; }
        public int CountMine { get; set; }
        public int CountUnassigned { get; set; }
    }
}

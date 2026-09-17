using System.Runtime.CompilerServices;

// 테스트 어셈블리만 internal을 본다. 리플렉션으로 private을 뚫는 대신 컴파일러가 이름 변경을 잡게 한다
[assembly: InternalsVisibleTo("Game.Core.Tests")]

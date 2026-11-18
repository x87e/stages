' ============================================================================
'  NamePatterns.vb
'  The name filters typed into Live folder and Running tasks options, e.g.
'      *.pdf; *.docx        report        ~$*, *.tmp
'
'  Rules (the same everywhere, and explained in the options dialog):
'    * Patterns are separated by ; or , - spaces around them are ignored.
'    * * means "any characters" and ? means "any one character", as in Explorer.
'    * A pattern with no * or ? matches any name that CONTAINS it ("report"
'      matches "Q3 report final.docx"), which is what people usually mean.
'    * Upper and lower case never matter.
'    * An empty list matches nothing; callers treat "no include patterns" as
'      "include everything" themselves.
'
'  Built once from the text (a regular expression per pattern), then used for
'  every name, so filtering thousands of files stays quick. Immutable, so one
'  set can be used from the scan thread while the UI thread builds a new one.
' ============================================================================
Imports System.Text.RegularExpressions

Namespace Services

    Public NotInheritable Class NamePatterns

        ''' <summary>The characters that separate one pattern from the next.</summary>
        Private Shared ReadOnly PatternSeparators As Char() = {";"c, ","c}
        Private Const AnyCharactersWildcard As String = "*"
        Private Const AnyOneCharacterWildcard As String = "?"
        ' What each wildcard becomes in a regular expression.
        Private Const AnyCharactersExpression As String = ".*"
        Private Const AnyOneCharacterExpression As String = "."
        ' A whole-name match: from the start (^) to the end ($) of the name.
        Private Const WholeNameStart As String = "^"
        Private Const WholeNameEnd As String = "$"

        Private ReadOnly _expressions As List(Of Regex)

        ''' <param name="patternText">The text as typed, e.g. "*.pdf; *.docx". Nothing or empty = no patterns.</param>
        Public Sub New(patternText As String)
            _expressions = New List(Of Regex)()
            If String.IsNullOrWhiteSpace(patternText) Then Return

            For Each rawPattern As String In patternText.Split(PatternSeparators, StringSplitOptions.RemoveEmptyEntries)
                Dim trimmedPattern As String = rawPattern.Trim()
                If trimmedPattern.Length = 0 Then Continue For
                _expressions.Add(New Regex(ToExpression(trimmedPattern), RegexOptions.IgnoreCase Or RegexOptions.CultureInvariant))
            Next
        End Sub

        ''' <summary>True if no patterns were typed.</summary>
        Public ReadOnly Property IsEmpty As Boolean
            Get
                Return _expressions.Count = 0
            End Get
        End Property

        ''' <summary>True if <paramref name="name"/> matches at least one pattern.</summary>
        Public Function Matches(name As String) As Boolean
            If String.IsNullOrEmpty(name) Then Return False
            For Each expression As Regex In _expressions
                If expression.IsMatch(name) Then Return True
            Next
            Return False
        End Function

        ''' <summary>
        ''' Turns one typed pattern into a regular expression. Everything except the two
        ''' wildcards is escaped, so a "." or "(" in a pattern means just that character.
        ''' </summary>
        Private Shared Function ToExpression(typedPattern As String) As String
            Dim hasWildcard As Boolean = typedPattern.Contains(AnyCharactersWildcard) OrElse typedPattern.Contains(AnyOneCharacterWildcard)
            ' Escape first, then turn the (escaped) wildcards back into their meanings.
            Dim escaped As String = Regex.Escape(typedPattern)
            escaped = escaped.Replace(Regex.Escape(AnyCharactersWildcard), AnyCharactersExpression)
            escaped = escaped.Replace(Regex.Escape(AnyOneCharacterWildcard), AnyOneCharacterExpression)

            If hasWildcard Then
                Return WholeNameStart & escaped & WholeNameEnd ' "*.pdf" must match the whole name
            End If
            Return escaped ' no wildcard: "contains", so no start/end anchors
        End Function

    End Class

End Namespace

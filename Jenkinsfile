pipeline {
    agent {
        docker { image 'mcr.microsoft.com/dotnet/sdk:8.0' }
    }

    stages {
        stage('Checkout') {
            steps {
                // 从Git仓库拉取代码
                checkout scm
            }
        }
        
        stage('Build') {
            steps {
                sh "dotnet build"
            }
        }

        stage('Test') {
            steps {
                sh "dotnet test"
            }
        }
    }
    
}